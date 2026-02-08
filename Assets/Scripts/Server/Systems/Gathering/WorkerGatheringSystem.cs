using Unity.Entities;
using Unity.NetCode;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;
using Shared;

namespace Server
{
    /// <summary>
    /// Worker 채집 사이클 처리 시스템 (서버)
    /// <para>순서: MovingToNode → Gathering → MovingToReturn → Unloading → (WaitingForNode) → 반복</para>
    /// <para>상태: Intent.Gather + WorkerState.Phase 조합으로 세부 단계 추적</para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(HandleBuildRequestSystem))]
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
        private ComponentLookup<PhysicsVelocity> _physicsVelocityLookup;
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;

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
            _physicsVelocityLookup = state.GetComponentLookup<PhysicsVelocity>(false);
            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // HandleBuildRequestSystem의 ExecuteBuildRequestJob이 UserCurrency에 쓰기 작업을 스케줄링하므로
            // 해당 Job이 완료된 후에 UserCurrencyLookup에 접근해야 함
            state.CompleteDependency();

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

            // --- 0. 채집 중단 감지 및 점유 해제 ---
            ProcessGatheringCancellation(ref state);

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

        [BurstCompile]
        private void UpdateLookups(ref SystemState state)
        {
            _resourceNodeStateLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _userCurrencyLookup.Update(ref state);
            _gatheringAbilityLookup.Update(ref state);
            _workRangeLookup.Update(ref state);
            _resourceNodeSettingLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);
            _physicsVelocityLookup.Update(ref state);
            _ghostOwnerLookup.Update(ref state);
        }

        /// <summary>
        /// [단계 0] 채집 중단 감지 및 점유 해제
        /// Intent가 Gather가 아닌데 노드를 점유하고 있는 워커의 점유를 해제
        /// </summary>
        [BurstCompile]
        private void ProcessGatheringCancellation(ref SystemState state)
        {
            foreach (var (intentState, gatherTarget, entity)
                in SystemAPI.Query<RefRO<UnitIntentState>, RefRO<GatheringTarget>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                // Intent가 Gather가 아니면 점유 해제 필요
                if (intentState.ValueRO.State == Intent.Gather) continue;

                Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                if (nodeEntity == Entity.Null) continue;
                if (!_resourceNodeStateLookup.HasComponent(nodeEntity)) continue;

                RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);

                // 이 워커가 점유 중이면 해제
                if (nodeStateRW.ValueRO.OccupyingWorker == entity)
                {
                    nodeStateRW.ValueRW.OccupyingWorker = Entity.Null;
                }
            }
        }

        /// <summary>
        /// [단계 1] 자원 노드로 이동 중 도착 감지
        /// 도착 판정: 노드 중심과의 거리 기준 (NavMesh가 표면까지 도달 못해도 채굴 가능)
        /// 이미 자원을 들고 있으면 채굴 없이 바로 반납으로 전환
        /// </summary>
        [BurstCompile]
        private void ProcessMovingToGather(ref SystemState state)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, movementGoal, transform, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRW<GatheringTarget>, RefRW<MovementGoal>, RefRO<LocalTransform>>()
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

                if (distance <= GetArrivalDistance(nodeEntity, entity))
                {
                    // 이미 자원을 들고 있으면 채굴 없이 바로 반납으로 전환
                    if (workerState.ValueRO.CarriedAmount > 0)
                    {
                        TransitionToReturn(ref state, ref intentState.ValueRW, ref actionState.ValueRW,
                            ref workerState.ValueRW, gatherTarget, movementGoal, entity, nodePos);
                    }
                    else
                    {
                        // 도착: 점유 상태 확인 후 처리
                        if (!_resourceNodeStateLookup.HasComponent(nodeEntity)) continue;

                        RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);
                        Entity currentOccupier = nodeStateRW.ValueRO.OccupyingWorker;

                        // 점유 안됨 또는 자기 자신이 점유 중 → 점유 설정 + Gathering
                        if (currentOccupier == Entity.Null || currentOccupier == entity)
                        {
                            if (currentOccupier != entity)
                            {
                                nodeStateRW.ValueRW.OccupyingWorker = entity;
                            }

                            workerState.ValueRW.Phase = GatherPhase.Gathering;
                            actionState.ValueRW.State = Action.Working;
                            workerState.ValueRW.GatheringProgress = 0f;

                            // 이동 비활성화 (채집 중에는 멈춤)
                            SystemAPI.SetComponentEnabled<MovementWaypoints>(entity, false);

                            // PhysicsVelocity 초기화 (관성으로 인한 이동 방지)
                            if (_physicsVelocityLookup.HasComponent(entity))
                            {
                                _physicsVelocityLookup.GetRefRW(entity).ValueRW.Linear = float3.zero;
                            }
                        }
                        // 다른 워커가 점유 중 → WaitingForNode로 전환
                        else
                        {
                            workerState.ValueRW.Phase = GatherPhase.WaitingForNode;
                            // Action은 Moving 유지 (근처에서 대기)
                        }
                    }
                }
            }
        }

        /// <summary>
        /// [단계 2] 자원 채집 진행 (시간 소요)
        /// CarriedResource 시각화는 CarriedResourceFollowSystem에서 Scale로 제어
        /// </summary>
        [BurstCompile]
        private void ProcessGathering(ref SystemState state, float deltaTime)
        {
            foreach (var (intentState, actionState, workerState, gatherTarget, ability, movementGoal, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>, RefRW<WorkerState>,
                    RefRW<GatheringTarget>, RefRO<GatheringAbility>, RefRW<MovementGoal>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                if (intentState.ValueRO.State != Intent.Gather) continue;
                if (workerState.ValueRO.Phase != GatherPhase.Gathering) continue;

                Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                if (nodeEntity == Entity.Null)
                {
                    SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                    continue;
                }

                if (!_resourceNodeStateLookup.HasComponent(nodeEntity)) continue;
                if (!_resourceNodeSettingLookup.HasComponent(nodeEntity)) continue;

                var setting = _resourceNodeSettingLookup[nodeEntity];
                float baseDuration = setting.BaseGatherDuration > 0.001f ? setting.BaseGatherDuration : 0.1f;
                float speed = ability.ValueRO.GatheringSpeed;

                workerState.ValueRW.GatheringProgress += (deltaTime * speed) / baseDuration;

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

                    float3 nodePos = _transformLookup[nodeEntity].Position;
                    TransitionToReturn(ref state, ref intentState.ValueRW, ref actionState.ValueRW,
                        ref workerState.ValueRW, gatherTarget, movementGoal, entity, nodePos);
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
                float3 centerPos = _transformLookup[returnPoint].Position;
                float distance = math.distance(workerPos, centerPos);

                if (distance <= GetArrivalDistance(returnPoint, entity))
                {
                    workerState.ValueRW.Phase = GatherPhase.Unloading;
                    actionState.ValueRW.State = Action.Working;
                    workerState.ValueRW.GatheringProgress = 0f;
                }
            }
        }

        /// <summary>
        /// [단계 4] 자원 하차 진행 (시간 소요) 및 재작업 결정
        /// CarriedResource 시각화는 CarriedResourceFollowSystem에서 Scale로 제어 (CarriedAmount = 0 → Scale = 0)
        /// </summary>
        [BurstCompile]
        private void ProcessUnloading(ref SystemState state, float deltaTime,
            NativeParallelHashMap<int, Entity> networkIdToCurrency)
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

                if (workerState.ValueRO.GatheringProgress >= 1.0f)
                {
                    if (networkIdToCurrency.TryGetValue(ghostOwner.ValueRO.NetworkId, out Entity currencyEntity))
                    {
                        if (_userCurrencyLookup.HasComponent(currencyEntity))
                        {
                            _userCurrencyLookup.GetRefRW(currencyEntity).ValueRW.Amount += workerState.ValueRO.CarriedAmount;
                        }
                    }

                    workerState.ValueRW.CarriedAmount = 0;
                    workerState.ValueRW.CarriedType = ResourceType.None;
                    workerState.ValueRW.GatheringProgress = 0f;

                    DecideNextAction(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW,
                        gatherTarget, movementGoal, entity);

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
            if (!gatherTarget.ValueRO.AutoReturn)
            {
                SetIdleState(ref intentState, ref actionState, ref workerState);
                return;
            }

            Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
            if (nodeEntity == Entity.Null)
            {
                nodeEntity = gatherTarget.ValueRO.LastGatheredNodeEntity;
            }

            if (nodeEntity == Entity.Null)
            {
                SetIdleState(ref intentState, ref actionState, ref workerState);
                return;
            }

            if (!_transformLookup.HasComponent(nodeEntity) || !_resourceNodeStateLookup.HasComponent(nodeEntity))
            {
                SetIdleState(ref intentState, ref actionState, ref workerState);
                gatherTarget.ValueRW.ResourceNodeEntity = Entity.Null;
                gatherTarget.ValueRW.LastGatheredNodeEntity = Entity.Null;
                return;
            }

            gatherTarget.ValueRW.ResourceNodeEntity = nodeEntity;

            RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);

            float3 workerPos = _transformLookup[workerEntity].Position;
            float3 nodePos = _transformLookup[nodeEntity].Position;
            float3 targetPos = CalculateApproachPoint(workerPos, nodePos, nodeEntity, workerEntity);

            // 노드가 비었거나 자기 자신이 점유 중이면 -> 즉시 점유 및 이동
            if (nodeStateRW.ValueRO.OccupyingWorker == Entity.Null ||
                nodeStateRW.ValueRO.OccupyingWorker == workerEntity)
            {
                nodeStateRW.ValueRW.OccupyingWorker = workerEntity;
                workerState.Phase = GatherPhase.MovingToNode;
                actionState.State = Action.Moving;

                movementGoal.ValueRW.Destination = targetPos;
                movementGoal.ValueRW.IsPathDirty = true;
            }
            // 다른 워커가 점유 중이면 -> 대기 상태로 노드 근처 이동
            else
            {
                workerState.Phase = GatherPhase.WaitingForNode;
                actionState.State = Action.Moving;

                movementGoal.ValueRW.Destination = targetPos;
                movementGoal.ValueRW.IsPathDirty = true;
            }
        }

        /// <summary>
        /// [단계 5] 대기 중 노드 선점 시도 (WaitingForNode)
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

                float3 workerPos = transform.ValueRO.Position;
                float3 nodePos = _transformLookup[nodeEntity].Position;
                float distance = math.distance(workerPos, nodePos);

                if (distance > GetArrivalDistance(nodeEntity, entity))
                {
                    continue;
                }

                // 이미 자원을 들고 있으면 채집 없이 바로 반납으로 전환
                if (workerState.ValueRO.CarriedAmount > 0)
                {
                    TransitionToReturn(ref state, ref intentState.ValueRW, ref actionState.ValueRW,
                        ref workerState.ValueRW, gatherTarget, movementGoal, entity, nodePos);
                    continue;
                }

                RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(nodeEntity);

                // 빈 자리 또는 자기 자신이 점유 중 → 즉시 Gathering 시작
                if (nodeStateRW.ValueRO.OccupyingWorker == Entity.Null ||
                    nodeStateRW.ValueRO.OccupyingWorker == entity)
                {
                    nodeStateRW.ValueRW.OccupyingWorker = entity;
                    workerState.ValueRW.Phase = GatherPhase.Gathering;
                    actionState.ValueRW.State = Action.Working;
                    workerState.ValueRW.GatheringProgress = 0f;

                    SystemAPI.SetComponentEnabled<MovementWaypoints>(entity, false);

                    // PhysicsVelocity 초기화 (관성으로 인한 이동 방지)
                    if (_physicsVelocityLookup.HasComponent(entity))
                    {
                        _physicsVelocityLookup.GetRefRW(entity).ValueRW.Linear = float3.zero;
                    }
                }
            }
        }

        /// <summary>
        /// 반납 이동 전환: Phase → MovingToReturn, 가장 가까운 ResourceCenter로 이동 목표 설정
        /// </summary>
        private void TransitionToReturn(
            ref SystemState state,
            ref UnitIntentState intentState,
            ref UnitActionState actionState,
            ref WorkerState workerState,
            RefRW<GatheringTarget> gatherTarget,
            RefRW<MovementGoal> movementGoal,
            Entity entity,
            float3 nodePos)
        {
            gatherTarget.ValueRW.LastGatheredNodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
            workerState.Phase = GatherPhase.MovingToReturn;
            actionState.State = Action.Moving;
            SystemAPI.SetComponentEnabled<MovementWaypoints>(entity, true);

            Entity nearestCenter = FindNearestResourceCenter(ref state, entity);
            Entity returnPoint = gatherTarget.ValueRO.ReturnPointEntity;
            if (nearestCenter != Entity.Null)
            {
                gatherTarget.ValueRW.ReturnPointEntity = nearestCenter;
                returnPoint = nearestCenter;
            }

            if (returnPoint != Entity.Null && _transformLookup.HasComponent(returnPoint))
            {
                float3 centerPos = _transformLookup[returnPoint].Position;
                float3 returnTargetPos = CalculateApproachPoint(nodePos, centerPos, returnPoint, entity);
                movementGoal.ValueRW.Destination = returnTargetPos;
                movementGoal.ValueRW.IsPathDirty = true;
            }
            else
            {
                SetIdleState(ref intentState, ref actionState, ref workerState);
            }
        }

        /// <summary>
        /// 도착 거리 계산: 타겟 반지름 + 유닛 반지름 + 여유분(1.5f)
        /// </summary>
        private float GetArrivalDistance(Entity targetEntity, Entity unitEntity)
        {
            float targetRadius = _obstacleRadiusLookup.HasComponent(targetEntity)
                ? _obstacleRadiusLookup[targetEntity].Radius : 1.5f;
            float unitRadius = _obstacleRadiusLookup.HasComponent(unitEntity)
                ? _obstacleRadiusLookup[unitEntity].Radius : 0.5f;
            return targetRadius + unitRadius + 1.5f;
        }

        /// <summary>
        /// Idle 상태로 전환
        /// </summary>
        private static void SetIdleState(ref UnitIntentState intentState, ref UnitActionState actionState, ref WorkerState workerState)
        {
            intentState.State = Intent.Idle;
            actionState.State = Action.Idle;
            workerState.Phase = GatherPhase.None;
        }

        /// <summary>
        /// 워커 현재 위치에서 가장 가까운 아군 ResourceCenter 찾기
        /// </summary>
        private Entity FindNearestResourceCenter(ref SystemState state, Entity workerEntity)
        {
            if (!_transformLookup.HasComponent(workerEntity)) return Entity.Null;
            if (!_ghostOwnerLookup.HasComponent(workerEntity)) return Entity.Null;

            int workerOwnerId = _ghostOwnerLookup[workerEntity].NetworkId;
            float3 workerPos = _transformLookup[workerEntity].Position;
            Entity nearest = Entity.Null;
            float minDist = float.MaxValue;

            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<ResourceCenterTag>()
                .WithEntityAccess())
            {
                // 소유권 검증: 같은 유저의 ResourceCenter만 선택
                if (!_ghostOwnerLookup.HasComponent(entity)) continue;
                if (_ghostOwnerLookup[entity].NetworkId != workerOwnerId) continue;

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
        /// fromPos에서 toPos를 향해 접근할 때, toPos 표면 직전 지점 계산
        /// </summary>
        private float3 CalculateApproachPoint(float3 fromPos, float3 toPos, Entity toEntity, Entity approacherEntity)
        {
            float3 direction = toPos - fromPos;
            float len = math.length(direction);
            if (len < 0.001f) return toPos;
            direction /= len;

            float toRadius = _obstacleRadiusLookup.HasComponent(toEntity)
                ? _obstacleRadiusLookup[toEntity].Radius : 1.5f;
            float approacherRadius = _obstacleRadiusLookup.HasComponent(approacherEntity)
                ? _obstacleRadiusLookup[approacherEntity].Radius : 0.5f;

            return toPos - direction * (toRadius + approacherRadius + 0.1f);
        }
    }
}
