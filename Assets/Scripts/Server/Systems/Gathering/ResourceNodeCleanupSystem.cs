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
        private ComponentLookup<UnitState> _unitStateLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            _unitStateLookup = state.GetComponentLookup<UnitState>(true);
            _healthLookup = state.GetComponentLookup<Health>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // 이전 Job들이 완료될 때까지 대기 (ServerDeathSystem 등)
            state.Dependency.Complete();

            _unitStateLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _transformLookup.Update(ref state);

            // 1. ResourceNode의 점유 Worker가 유효한지 확인
            foreach (var (nodeState, entity) in SystemAPI.Query<RefRW<ResourceNodeState>>()
                .WithAll<ResourceNodeTag>()
                .WithEntityAccess())
            {
                Entity occupyingWorker = nodeState.ValueRO.OccupyingWorker;
                if (occupyingWorker == Entity.Null) continue;

                // Worker가 더 이상 존재하지 않거나 사망한 경우 점유 해제
                if (!_unitStateLookup.HasComponent(occupyingWorker))
                {
                    nodeState.ValueRW.OccupyingWorker = Entity.Null;
                    continue;
                }

                var workerUnitState = _unitStateLookup[occupyingWorker];
                if (workerUnitState.CurrentState == UnitContext.Dead ||
                    workerUnitState.CurrentState == UnitContext.Dying)
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
                    }
                }
            }

            // 2. 채집 관련 상태인 Worker들의 타겟 유효성 확인
            foreach (var (unitState, gatherTarget, workerState, entity)
                in SystemAPI.Query<RefRW<UnitState>, RefRW<GatheringTarget>, RefRW<WorkerState>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                var currentState = unitState.ValueRO.CurrentState;

                // MovingToGather 또는 Gathering 상태에서 ResourceNode 유효성 확인
                if (currentState == UnitContext.MovingToGather ||
                    currentState == UnitContext.Gathering)
                {
                    Entity nodeEntity = gatherTarget.ValueRO.ResourceNodeEntity;
                    if (nodeEntity == Entity.Null)
                    {
                        // 타겟이 없으면 Idle로
                        unitState.ValueRW.CurrentState = UnitContext.Idle;
                        workerState.ValueRW.IsInsideNode = false;
                        continue;
                    }

                    // ResourceNode가 더 이상 존재하지 않는 경우
                    if (!_transformLookup.HasComponent(nodeEntity))
                    {
                        unitState.ValueRW.CurrentState = UnitContext.Idle;
                        workerState.ValueRW.IsInsideNode = false;
                        gatherTarget.ValueRW.ResourceNodeEntity = Entity.Null;
                    }
                }

                // MovingToReturn 상태에서 ReturnPoint 유효성 확인
                if (currentState == UnitContext.MovingToReturn)
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
                            unitState.ValueRW.CurrentState = UnitContext.Idle;
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
                            unitState.ValueRW.CurrentState = UnitContext.Idle;
                        }
                    }
                }
            }
        }

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
    }
}
