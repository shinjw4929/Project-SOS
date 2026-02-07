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
    /// 자원 노드 정리 시스템 (서버)
    /// - Worker 사망 시 점유 해제
    /// - 채집 중인 Worker의 타겟 유효성 검사
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ResourceNodeCleanupSystem : ISystem
    {
        private ComponentLookup<UnitActionState> _unitActionStateLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<UnitIntentState> _unitIntentStateLookup;
        private ComponentLookup<GatheringTarget> _gatheringTargetLookup;
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            _unitActionStateLookup = state.GetComponentLookup<UnitActionState>(true);
            _healthLookup = state.GetComponentLookup<Health>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _unitIntentStateLookup = state.GetComponentLookup<UnitIntentState>(true);
            _gatheringTargetLookup = state.GetComponentLookup<GatheringTarget>(true);
            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // 이전 Job들이 완료될 때까지 대기 (ServerDeathSystem 등)
            state.Dependency.Complete();

            _unitActionStateLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _gatheringTargetLookup.Update(ref state);
            _ghostOwnerLookup.Update(ref state);

            // 1. ResourceNode의 점유 Worker가 유효한지 확인
            foreach (var (nodeState, entity) in SystemAPI.Query<RefRW<ResourceNodeState>>()
                .WithAll<ResourceNodeTag>()
                .WithEntityAccess())
            {
                Entity occupyingWorker = nodeState.ValueRO.OccupyingWorker;
                if (occupyingWorker == Entity.Null) continue;

                // Worker가 더 이상 존재하지 않는 경우 점유 해제
                if (!_unitActionStateLookup.HasComponent(occupyingWorker))
                {
                    nodeState.ValueRW.OccupyingWorker = Entity.Null;
                    continue;
                }

                // Worker가 사망한 경우 점유 해제
                var workerActionState = _unitActionStateLookup[occupyingWorker];
                if (workerActionState.State == Action.Dead ||
                    workerActionState.State == Action.Dying)
                {
                    nodeState.ValueRW.OccupyingWorker = Entity.Null;
                    continue;
                }

                // Worker의 HP가 0 이하인 경우
                if (_healthLookup.HasComponent(occupyingWorker))
                {
                    if (_healthLookup[occupyingWorker].CurrentValue <= 0)
                    {
                        nodeState.ValueRW.OccupyingWorker = Entity.Null;
                        continue;
                    }
                }

                // Worker의 IntentState가 Gather가 아닌 경우 점유 해제
                if (_unitIntentStateLookup.HasComponent(occupyingWorker))
                {
                    var intentState = _unitIntentStateLookup[occupyingWorker];
                    if (intentState.State != Intent.Gather)
                    {
                        nodeState.ValueRW.OccupyingWorker = Entity.Null;
                        continue;
                    }
                }

                // Worker의 GatheringTarget이 이 노드가 아닌 경우 점유 해제
                if (_gatheringTargetLookup.HasComponent(occupyingWorker))
                {
                    var gatheringTarget = _gatheringTargetLookup[occupyingWorker];
                    if (gatheringTarget.ResourceNodeEntity != entity)
                    {
                        nodeState.ValueRW.OccupyingWorker = Entity.Null;
                        continue;
                    }
                }
                else
                {
                    // GatheringTarget 컴포넌트가 없으면 점유 해제
                    nodeState.ValueRW.OccupyingWorker = Entity.Null;
                }
            }

            // 2. 채집 관련 상태인 Worker들의 타겟 유효성 확인
            foreach (var (intentState, actionState, gatherTarget, workerState, entity)
                in SystemAPI.Query<RefRW<UnitIntentState>, RefRW<UnitActionState>,
                    RefRW<GatheringTarget>, RefRW<WorkerState>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                // Intent.Gather가 아니면 스킵
                if (intentState.ValueRO.State != Intent.Gather) continue;

                var currentPhase = workerState.ValueRO.Phase;

                // MovingToNode 또는 Gathering 상태에서 ResourceNode 유효성 확인
                if (currentPhase == GatherPhase.MovingToNode ||
                    currentPhase == GatherPhase.Gathering)
                {
                    Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                    if (nodeEntity == Entity.Null)
                    {
                        // 타겟이 없으면 Idle로
                        SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                        continue;
                    }

                    // ResourceNode가 더 이상 존재하지 않는 경우
                    if (!_transformLookup.HasComponent(nodeEntity))
                    {
                        SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                        gatherTarget.ValueRW.ResourceNodeEntity = Entity.Null;
                    }
                }

                // MovingToReturn 상태에서 ReturnPoint 유효성 확인
                if (currentPhase == GatherPhase.MovingToReturn)
                {
                    Entity returnPoint = gatherTarget.ValueRO.ReturnPointEntity;
                    if (returnPoint == Entity.Null)
                    {
                        // 반납 지점이 없으면 가장 가까운 ResourceCenter 찾기
                        Entity newReturnPoint = FindNearestResourceCenter(ref state, entity);
                        if (newReturnPoint != Entity.Null)
                        {
                            gatherTarget.ValueRW.ReturnPointEntity = newReturnPoint;
                        }
                        else
                        {
                            // ResourceCenter가 없으면 Idle로 (자원은 유지)
                            SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                        }
                        continue;
                    }

                    // ReturnPoint가 더 이상 존재하지 않는 경우
                    if (!_transformLookup.HasComponent(returnPoint))
                    {
                        // 새로운 ResourceCenter 찾기
                        Entity newReturnPoint = FindNearestResourceCenter(ref state, entity);
                        if (newReturnPoint != Entity.Null)
                        {
                            gatherTarget.ValueRW.ReturnPointEntity = newReturnPoint;
                        }
                        else
                        {
                            SetIdleState(ref intentState.ValueRW, ref actionState.ValueRW, ref workerState.ValueRW);
                        }
                    }
                }
            }
        }

        private static void SetIdleState(ref UnitIntentState intentState, ref UnitActionState actionState, ref WorkerState workerState)
        {
            intentState.State = Intent.Idle;
            actionState.State = Action.Idle;
            workerState.Phase = GatherPhase.None;
        }

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
    }
}
