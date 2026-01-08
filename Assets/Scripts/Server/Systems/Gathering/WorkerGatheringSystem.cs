using Unity.Entities;
using Unity.NetCode;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Shared;

namespace Server
{
    /// <summary>
    /// Worker 채집 사이클 처리 시스템 (서버)
    /// <para>순서: MovingToNode → Gathering → MovingToReturn → Unloading → (WaitingForNode) → 반복</para>
    /// <para>상태: Intent.Gather + WorkerState.Phase 조합으로 세부 단계 추적</para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct WorkerGatheringSystem : ISystem
    {
        // --- Lookups ---
        private ComponentLookup<ResourceNodeState> _resourceNodeStateLookup;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<UserCurrency> _userCurrencyLookup;
        private ComponentLookup<GatheringAbility> _gatheringAbilityLookup;
        private ComponentLookup<WorkRange> _workRangeLookup;
        private ComponentLookup<ResourceNodeSetting> _resourceNodeSettingLookup;
        private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            _resourceNodeStateLookup = state.GetComponentLookup<ResourceNodeState>(false);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _userCurrencyLookup = state.GetComponentLookup<UserCurrency>(false);
            _gatheringAbilityLookup = state.GetComponentLookup<GatheringAbility>(true);
            _workRangeLookup = state.GetComponentLookup<WorkRange>(true);
            _resourceNodeSettingLookup = state.GetComponentLookup<ResourceNodeSetting>(true);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            UpdateLookups(ref state);

            float deltaTime = SystemAPI.Time.DeltaTime;

            // 네트워크 ID → UserCurrency Entity 맵 (임시 할당)
            var networkIdToCurrency = new NativeParallelHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                .WithAll<UserEconomyTag>()
                .WithEntityAccess())
            {
                networkIdToCurrency.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
            }

            // --- 1. 채집지로 이동 (Phase.MovingToNode) ---
            ProcessMovingToGather(ref state);

            // --- 2. 채집 진행 (Phase.Gathering) ---
            ProcessGathering(ref state, deltaTime);

            // --- 3. 반납지로 이동 (Phase.MovingToReturn) ---
            ProcessMovingToReturn(ref state);

            // --- 4. 자원 하차 및 정산 (Phase.Unloading) ---
            ProcessUnloading(ref state, deltaTime, networkIdToCurrency);

            // --- 5. 대기열 처리 (Phase.WaitingForNode) ---
            ProcessWaitingForNode(ref state);

            networkIdToCurrency.Dispose();
        }

        private void UpdateLookups(ref SystemState state)
        {
            _resourceNodeStateLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _userCurrencyLookup.Update(ref state);
            _gatheringAbilityLookup.Update(ref state);
            _workRangeLookup.Update(ref state);
            _resourceNodeSettingLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);
        }

        /// <summary>
        /// [단계 1] 자원 노드로 이동 중 도착 감지
        /// </summary>
        [BurstCompile]
        private void ProcessMovingToGather(ref SystemState state)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, transform, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRO<GatheringTarget>, RefRO<LocalTransform>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                // Intent.Gather + Phase.MovingToNode 조합으로 상태 확인
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.MovingToNode) continue;

                Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                if (nodeEntity == Entity.Null || !_transformLookup.HasComponent(nodeEntity)) continue;

                float3 workerPos = transform.ValueRO.Position;
                float3 nodePos = _transformLookup[nodeEntity].Position;
                float distance = math.distance(workerPos, nodePos);

                // WorkRange가 있으면 사용, 없으면 기본값 1.0f
                float workRange = _workRangeLookup.HasComponent(entity) ? _workRangeLookup[entity].Value : 1.0f;

                if (distance <= workRange)
                {
                    // 도착: Gathering 상태로 전환
                    workerState.ValueRW.Phase = GatherPhase.Gathering;
                    actionState.ValueRW.State = Action.Working;
                    workerState.ValueRW.IsInsideNode = true;
                    workerState.ValueRW.GatheringProgress = 0f;
                }
            }
        }

        /// <summary>
        /// [단계 2] 자원 채집 진행 (시간 소요)
        /// </summary>
        [BurstCompile]
        private void ProcessGathering(ref SystemState state, float deltaTime)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, ability, movementGoal, ghostOwner, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRW<GatheringTarget>, RefRO<GatheringAbility>, RefRW<MovementGoal>, RefRO<GhostOwner>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.Gathering) continue;

                Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                if (nodeEntity == Entity.Null)
                {
                    // 노드가 없으면 Idle로 전환
                    SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                    continue;
                }

                if (!_resourceNodeStateLookup.HasComponent(nodeEntity)) continue;
                if (!_resourceNodeSettingLookup.HasComponent(nodeEntity)) continue;

                // 노드 설정 가져오기
                var setting = _resourceNodeSettingLookup[nodeEntity];
                float baseDuration = setting.BaseGatherDuration > 0.001f ? setting.BaseGatherDuration : 0.1f;
                float speed = ability.ValueRO.GatheringSpeed;

                // 진행도 업데이트
                workerState.ValueRW.GatheringProgress += (deltaTime * speed) / baseDuration;

                // 완료 처리
                if (workerState.ValueRO.GatheringProgress >= 1.0f)
                {
                    int newAmount = workerState.ValueRO.CarriedAmount + setting.AmountPerGather;
                    int maxAmount = ability.ValueRO.MaxCarryAmount;

                    workerState.ValueRW.CarriedAmount = math.min(newAmount, maxAmount);
                    workerState.ValueRW.CarriedType = setting.ResourceType;
                    workerState.ValueRW.GatheringProgress = 0f;

                    // 노드 점유 해제
                    if (_resourceNodeStateLookup.HasComponent(nodeEntity))
                    {
                        RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);
                        if (nodeStateRW.ValueRO.OccupyingWorker == entity)
                        {
                            nodeStateRW.ValueRW.OccupyingWorker = Entity.Null;
                        }
                    }

                    // 복귀 이동 시작
                    workerState.ValueRW.Phase = GatherPhase.MovingToReturn;
                    actionState.ValueRW.State = Action.Moving;
                    workerState.ValueRW.IsInsideNode = false;

                    Entity returnPoint = gatherTarget.ValueRO.ReturnPointEntity;
                    if (returnPoint != Entity.Null && _transformLookup.HasComponent(returnPoint))
                    {
                        float3 returnPos = _transformLookup[returnPoint].Position;
                        movementGoal.ValueRW.Destination = returnPos;
                        movementGoal.ValueRW.IsPathDirty = true;
                    }
                }
            }
        }

        /// <summary>
        /// [단계 3] 반납 지점으로 이동 중 도착 감지
        /// </summary>
        [BurstCompile]
        private void ProcessMovingToReturn(ref SystemState state)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, transform, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRO<GatheringTarget>, RefRO<LocalTransform>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.MovingToReturn) continue;

                Entity returnPoint = gatherTarget.ValueRO.ReturnPointEntity;
                if (returnPoint == Entity.Null || !_transformLookup.HasComponent(returnPoint))
                {
                    SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                    continue;
                }

                float3 workerPos = transform.ValueRO.Position;
                float3 returnPos = _transformLookup[returnPoint].Position;
                float distance = math.distance(workerPos, returnPos);

                float touchingDistance = 0.5f;

                // 1. 타겟(건물)의 반지름 가져오기
                float targetRadius = 1.5f; // 기본값 (ResourceCenter:2.84 등 고려)
                if (_obstacleRadiusLookup.HasComponent(returnPoint))
                {
                    targetRadius = _obstacleRadiusLookup[returnPoint].Radius;
                }

                // 2. 나(유닛)의 반지름 가져오기
                float myRadius = 0.5f; // 컴포넌트 없을 때 기본값
                if (_obstacleRadiusLookup.HasComponent(entity))
                {
                    myRadius = _obstacleRadiusLookup[entity].Radius;
                }

                touchingDistance = targetRadius + myRadius + 0.1f;

                if (distance <= touchingDistance)
                {
                    // 도착: 하차(Unloading) 상태로 전환
                    workerState.ValueRW.Phase = GatherPhase.Unloading;
                    actionState.ValueRW.State = Action.Working;
                    workerState.ValueRW.GatheringProgress = 0f;
                }
            }
        }

        /// <summary>
        /// [단계 4] 자원 하차 진행 (시간 소요) 및 재작업 결정
        /// </summary>
        [BurstCompile]
        private void ProcessUnloading(ref SystemState state, float deltaTime, NativeParallelHashMap<int, Entity> networkIdToCurrency)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, ability, movementGoal, ghostOwner, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRW<GatheringTarget>, RefRO<GatheringAbility>, RefRW<MovementGoal>, RefRO<GhostOwner>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.Unloading) continue;

                float unloadDuration = ability.ValueRO.UnloadDuration > 0 ? ability.ValueRO.UnloadDuration : 1.0f;

                workerState.ValueRW.GatheringProgress += deltaTime / unloadDuration;

                // 하차 완료
                if (workerState.ValueRO.GatheringProgress >= 1.0f)
                {
                    // 자원 지급
                    if (networkIdToCurrency.TryGetValue(ghostOwner.ValueRO.NetworkId, out Entity currencyEntity))
                    {
                        if (_userCurrencyLookup.HasComponent(currencyEntity))
                        {
                            _userCurrencyLookup.GetRefRW(currencyEntity).ValueRW.Amount += workerState.ValueRO.CarriedAmount;
                        }
                    }

                    // 상태 초기화
                    workerState.ValueRW.CarriedAmount = 0;
                    workerState.ValueRW.CarriedType = ResourceType.None;
                    workerState.ValueRW.GatheringProgress = 0f;

                    // 다음 행동 결정
                    DecideNextAction(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW,
                        gatherTarget, movementGoal, entity);
                }
            }
        }

        /// <summary>
        /// 하차 후 자동 복귀 여부 및 대기 상태 결정
        /// </summary>
        private void DecideNextAction(
            ref UnitIntentState intentState,
            ref UnitActionState actionState,
            ref WorkerState workerState,
            RefRW<GatheringTarget> gatherTarget,
            RefRW<MovementGoal> movementGoal,
            Entity workerEntity)
        {
            if (!gatherTarget.ValueRO.AutoReturn || gatherTarget.ValueRO.ResourceNodeEntity == Entity.Null)
            {
                SetIdleStateRef(ref intentState, ref actionState, ref workerState);
                return;
            }

            Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;

            // 노드가 유효하지 않으면 중단
            if (!_transformLookup.HasComponent(nodeEntity) || !_resourceNodeStateLookup.HasComponent(nodeEntity))
            {
                SetIdleStateRef(ref intentState, ref actionState, ref workerState);
                gatherTarget.ValueRW.ResourceNodeEntity = Entity.Null;
                return;
            }

            RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);

            // A. 노드가 비었으면 -> 즉시 점유 및 이동
            if (nodeStateRW.ValueRO.OccupyingWorker == Entity.Null)
            {
                nodeStateRW.ValueRW.OccupyingWorker = workerEntity;
                workerState.Phase = GatherPhase.MovingToNode;
                actionState.State = Action.Moving;
                // Intent는 Gather 유지

                movementGoal.ValueRW.Destination = _transformLookup[nodeEntity].Position;
                movementGoal.ValueRW.IsPathDirty = true;
            }
            // B. 노드가 찼으면 -> 대기 상태로 노드 근처 이동
            else
            {
                workerState.Phase = GatherPhase.WaitingForNode;
                actionState.State = Action.Idle;
                // Intent는 Gather 유지

                movementGoal.ValueRW.Destination = _transformLookup[nodeEntity].Position;
                movementGoal.ValueRW.IsPathDirty = true;
            }
        }

        /// <summary>
        /// [단계 5] 대기 중 노드 선점 시도 (WaitingForNode)
        /// </summary>
        [BurstCompile]
        private void ProcessWaitingForNode(ref SystemState state)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, movementGoal, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRW<GatheringTarget>, RefRW<MovementGoal>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.WaitingForNode) continue;

                Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;

                if (nodeEntity == Entity.Null || !_resourceNodeStateLookup.HasComponent(nodeEntity))
                {
                    SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                    gatherTarget.ValueRW.ResourceNodeEntity = Entity.Null;
                    continue;
                }

                RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);

                // 빈 자리 발견 (선착순 점유)
                if (nodeStateRW.ValueRO.OccupyingWorker == Entity.Null)
                {
                    nodeStateRW.ValueRW.OccupyingWorker = entity;
                    workerState.ValueRW.Phase = GatherPhase.MovingToNode;
                    actionState.ValueRW.State = Action.Moving;

                    // 목적지 재확인 (이미 근처여도 확실하게)
                    movementGoal.ValueRW.Destination = _transformLookup[nodeEntity].Position;
                    movementGoal.ValueRW.IsPathDirty = true;
                }
            }
        }

        /// <summary>
        /// Idle 상태로 전환 (RefRW 버전)
        /// </summary>
        private static void SetIdleState(ref UnitIntentState intentState, ref UnitActionState actionState, ref WorkerState workerState)
        {
            intentState.State = Intent.Idle;
            actionState.State = Action.Idle;
            workerState.Phase = GatherPhase.None;
            workerState.IsInsideNode = false;
        }

        /// <summary>
        /// Idle 상태로 전환 (ref 버전)
        /// </summary>
        private static void SetIdleStateRef(ref UnitIntentState intentState, ref UnitActionState actionState, ref WorkerState workerState)
        {
            intentState.State = Intent.Idle;
            actionState.State = Action.Idle;
            workerState.Phase = GatherPhase.None;
            workerState.IsInsideNode = false;
        }
    }
}
