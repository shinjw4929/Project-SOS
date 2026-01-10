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
        /// 도착 판정: 목적지(MovementGoal.Destination)와의 거리 기준
        /// </summary>
        [BurstCompile]
        private void ProcessMovingToGather(ref SystemState state)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, movementGoal, transform, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRO<GatheringTarget>, RefRO<MovementGoal>, RefRO<LocalTransform>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                // Intent.Gather + Phase.MovingToNode 조합으로 상태 확인
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.MovingToNode) continue;

                Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                if (nodeEntity == Entity.Null || !_transformLookup.HasComponent(nodeEntity)) continue;

                float3 workerPos = transform.ValueRO.Position;
                float3 targetPos = movementGoal.ValueRO.Destination; // 목적지 (표면 지점)
                float distance = math.distance(workerPos, targetPos);

                // 유닛 반지름 + 여유분
                float unitRadius = _obstacleRadiusLookup.HasComponent(entity)
                    ? _obstacleRadiusLookup[entity].Radius
                    : 0.5f;
                float arrivalDistance = unitRadius + 0.5f;

                if (distance <= arrivalDistance)
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

                    // 이동 활성화
                    SystemAPI.SetComponentEnabled<MovementWaypoints>(entity, true);

                    // ReturnPoint 재계산 (현재 위치에서 가장 가까운 ResourceCenter)
                    Entity nearestCenter = FindNearestResourceCenter(ref state, entity);
                    Entity returnPoint = gatherTarget.ValueRO.ReturnPointEntity;

                    // 가까운 센터가 있으면 갱신
                    if (nearestCenter != Entity.Null)
                    {
                        gatherTarget.ValueRW.ReturnPointEntity = nearestCenter;
                        returnPoint = nearestCenter;
                    }

                    if (returnPoint != Entity.Null && _transformLookup.HasComponent(returnPoint))
                    {
                        // ResourceNode → ResourceCenter 직선 상의 표면 지점을 목적지로 설정
                        float3 nodePos = _transformLookup[nodeEntity].Position;
                        float3 centerPos = _transformLookup[returnPoint].Position;
                        float3 targetPos = CalculateReturnTargetPosition(nodePos, centerPos, returnPoint, entity);

                        movementGoal.ValueRW.Destination = targetPos;
                        movementGoal.ValueRW.IsPathDirty = true;
                    }
                    else
                    {
                        // ResourceCenter가 없으면 Idle (자원은 유지)
                        SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                    }
                }
            }
        }

        /// <summary>
        /// [단계 3] 반납 지점으로 이동 중 도착 감지
        /// 도착 판정: ResourceCenter 중심 기준 (여러 워커 동시 반납 가능)
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
                float3 centerPos = _transformLookup[returnPoint].Position; // 중심 기준
                float distance = math.distance(workerPos, centerPos);

                // 센터 반지름 + 유닛 반지름 + 여유분 (패스파인딩 오차/분리력 고려)
                float centerRadius = _obstacleRadiusLookup.HasComponent(returnPoint)
                    ? _obstacleRadiusLookup[returnPoint].Radius
                    : 1.5f;
                float unitRadius = _obstacleRadiusLookup.HasComponent(entity)
                    ? _obstacleRadiusLookup[entity].Radius
                    : 0.5f;
                float arrivalDistance = centerRadius + unitRadius + 1.5f;

                if (distance <= arrivalDistance)
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

                    // 이동 시작 시 MovementWaypoints 활성화
                    if (workerState.ValueRO.Phase == GatherPhase.MovingToNode ||
                        workerState.ValueRO.Phase == GatherPhase.WaitingForNode)
                    {
                        SystemAPI.SetComponentEnabled<MovementWaypoints>(entity, true);
                    }
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

            // 노드 표면 지점 계산
            float3 workerPos = _transformLookup[workerEntity].Position;
            float3 nodePos = _transformLookup[nodeEntity].Position;
            float3 targetPos = CalculateNodeTargetPosition(workerPos, nodePos, nodeEntity, workerEntity);

            // A. 노드가 비었으면 -> 즉시 점유 및 이동
            if (nodeStateRW.ValueRO.OccupyingWorker == Entity.Null)
            {
                nodeStateRW.ValueRW.OccupyingWorker = workerEntity;
                workerState.Phase = GatherPhase.MovingToNode;
                actionState.State = Action.Moving;
                // Intent는 Gather 유지

                movementGoal.ValueRW.Destination = targetPos;
                movementGoal.ValueRW.IsPathDirty = true;
            }
            // B. 노드가 찼으면 -> 대기 상태로 노드 근처 이동
            else
            {
                workerState.Phase = GatherPhase.WaitingForNode;
                actionState.State = Action.Moving;
                // Intent는 Gather 유지

                movementGoal.ValueRW.Destination = targetPos;
                movementGoal.ValueRW.IsPathDirty = true;
            }
        }

        /// <summary>
        /// [단계 5] 대기 중 노드 선점 시도 (WaitingForNode)
        /// 노드 근처 도착 후 점유 해제되면 바로 채집 시작
        /// </summary>
        [BurstCompile]
        private void ProcessWaitingForNode(ref SystemState state)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, movementGoal, transform, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRW<GatheringTarget>, RefRW<MovementGoal>, RefRO<LocalTransform>>()
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

                if (!_transformLookup.HasComponent(nodeEntity)) continue;

                // 목적지(표면 지점) 기준 도착 확인
                float3 workerPos = transform.ValueRO.Position;
                float3 targetPos = movementGoal.ValueRO.Destination;
                float distance = math.distance(workerPos, targetPos);

                // 유닛 반지름 + 여유분
                float unitRadius = _obstacleRadiusLookup.HasComponent(entity)
                    ? _obstacleRadiusLookup[entity].Radius
                    : 0.5f;
                float arrivalDistance = unitRadius + 0.5f;

                // 아직 도착 안 함 - 이동 유지
                if (distance > arrivalDistance)
                {
                    continue;
                }

                RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);

                // 빈 자리 발견 (선착순 점유) → 즉시 Gathering 시작
                if (nodeStateRW.ValueRO.OccupyingWorker == Entity.Null)
                {
                    nodeStateRW.ValueRW.OccupyingWorker = entity;
                    workerState.ValueRW.Phase = GatherPhase.Gathering;
                    actionState.ValueRW.State = Action.Working;
                    workerState.ValueRW.IsInsideNode = true;
                    workerState.ValueRW.GatheringProgress = 0f;
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

        /// <summary>
        /// 워커 현재 위치에서 가장 가까운 ResourceCenter 찾기
        /// </summary>
        private Entity FindNearestResourceCenter(ref SystemState state, Entity workerEntity)
        {
            if (!_transformLookup.HasComponent(workerEntity)) return Entity.Null;

            float3 workerPos = _transformLookup[workerEntity].Position;
            Entity nearest = Entity.Null;
            float minDist = float.MaxValue;

            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<ResourceCenterTag>()
                .WithEntityAccess())
            {
                float dist = math.distance(workerPos, transform.ValueRO.Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = entity;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 워커 → ResourceNode 직선 상의 표면 지점 계산
        /// </summary>
        private float3 CalculateNodeTargetPosition(float3 workerPos, float3 nodePos, Entity nodeEntity, Entity workerEntity)
        {
            // 워커 → 노드 방향 벡터
            float3 direction = nodePos - workerPos;
            float len = math.length(direction);

            // 같은 위치면 노드 위치 반환
            if (len < 0.001f)
            {
                return nodePos;
            }

            direction = direction / len; // normalize

            // 노드 반지름
            float nodeRadius = _resourceNodeSettingLookup.HasComponent(nodeEntity)
                ? _resourceNodeSettingLookup[nodeEntity].Radius
                : 1.5f;

            // 유닛 반지름
            float unitRadius = _obstacleRadiusLookup.HasComponent(workerEntity)
                ? _obstacleRadiusLookup[workerEntity].Radius
                : 0.5f;

            // 노드 표면 지점 (노드 중심에서 워커 방향으로 반지름만큼 뺀 위치)
            float offset = nodeRadius + unitRadius + 0.1f;
            float3 targetPos = nodePos - direction * offset;

            return targetPos;
        }

        /// <summary>
        /// ResourceNode → ResourceCenter 직선 상의 표면 지점 계산
        /// 건물을 돌아가지 않고 직선으로 접근하기 위해 사용
        /// </summary>
        private float3 CalculateReturnTargetPosition(float3 nodePos, float3 centerPos, Entity centerEntity, Entity workerEntity)
        {
            // 노드 → 센터 방향 벡터
            float3 direction = centerPos - nodePos;
            float len = math.length(direction);

            // 같은 위치면 센터 위치 반환
            if (len < 0.001f)
            {
                return centerPos;
            }

            direction = direction / len; // normalize

            // 센터 반지름
            float centerRadius = _obstacleRadiusLookup.HasComponent(centerEntity)
                ? _obstacleRadiusLookup[centerEntity].Radius
                : 1.5f;

            // 유닛 반지름
            float unitRadius = _obstacleRadiusLookup.HasComponent(workerEntity)
                ? _obstacleRadiusLookup[workerEntity].Radius
                : 0.5f;

            // 센터 표면 지점 (센터 중심에서 노드 방향으로 반지름만큼 뺀 위치)
            float offset = centerRadius + unitRadius + 0.1f;
            float3 targetPos = centerPos - direction * offset;

            return targetPos;
        }
    }
}
