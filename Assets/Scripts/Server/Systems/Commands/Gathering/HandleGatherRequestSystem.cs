using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 자원 채집 요청 RPC 처리 시스템 (서버)
    /// - 점유 가능 여부 확인
    /// - 점유 설정 및 GatheringTarget 설정
    /// - UnitState = MovingToGather로 변경
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleGatherRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<WorkerTag> _workerTagLookup;
        [ReadOnly] private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;

        private ComponentLookup<ResourceNodeState> _resourceNodeStateLookup;
        private ComponentLookup<GatheringTarget> _gatheringTargetLookup;
        private ComponentLookup<UnitState> _unitStateLookup;
        private ComponentLookup<MovementGoal> _movementGoalLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _workerTagLookup = state.GetComponentLookup<WorkerTag>(true);
            _resourceNodeTagLookup = state.GetComponentLookup<ResourceNodeTag>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);

            _resourceNodeStateLookup = state.GetComponentLookup<ResourceNodeState>(false);
            _gatheringTargetLookup = state.GetComponentLookup<GatheringTarget>(false);
            _unitStateLookup = state.GetComponentLookup<UnitState>(false);
            _movementGoalLookup = state.GetComponentLookup<MovementGoal>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _ghostOwnerLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _workerTagLookup.Update(ref state);
            _resourceNodeTagLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _resourceCenterTagLookup.Update(ref state);
            _resourceNodeStateLookup.Update(ref state);
            _gatheringTargetLookup.Update(ref state);
            _unitStateLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostMap 생성
            var ghostMap = new NativeParallelHashMap<int, Entity>(1024, Allocator.Temp);
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithEntityAccess())
            {
                ghostMap.TryAdd(ghost.ValueRO.ghostId, entity);
            }

            // ResourceCenter 목록 수집 (가장 가까운 것 찾기 위해)
            var resourceCenters = new NativeList<Entity>(16, Allocator.Temp);
            foreach (var (_, entity) in SystemAPI.Query<RefRO<ResourceCenterTag>>().WithEntityAccess())
            {
                resourceCenters.Add(entity);
            }

            // RPC 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<GatherRequestRpc>>()
                .WithEntityAccess())
            {
                if (ghostMap.TryGetValue(rpc.ValueRO.WorkerGhostId, out Entity workerEntity) &&
                    ghostMap.TryGetValue(rpc.ValueRO.ResourceNodeGhostId, out Entity resourceNodeEntity))
                {
                    ProcessRequest(
                        ecb,
                        workerEntity,
                        resourceNodeEntity,
                        rpcReceive.ValueRO.SourceConnection,
                        rpc.ValueRO,
                        resourceCenters
                    );
                }

                ecb.DestroyEntity(rpcEntity);
            }

            resourceCenters.Dispose();
        }

        private void ProcessRequest(
            EntityCommandBuffer ecb,
            Entity workerEntity,
            Entity resourceNodeEntity,
            Entity sourceConnection,
            GatherRequestRpc rpc,
            NativeList<Entity> resourceCenters)
        {
            // 1. Worker 유효성 검증
            if (!_workerTagLookup.HasComponent(workerEntity) ||
                !_ghostOwnerLookup.HasComponent(workerEntity) ||
                !_networkIdLookup.HasComponent(sourceConnection))
                return;

            int ownerId = _ghostOwnerLookup[workerEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;
            if (ownerId != requesterId) return;

            // 2. ResourceNode 유효성 검증
            if (!_resourceNodeTagLookup.HasComponent(resourceNodeEntity) ||
                !_resourceNodeStateLookup.HasComponent(resourceNodeEntity))
                return;

            // 3. 점유 가능 여부 확인
            RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(resourceNodeEntity);
            if (nodeStateRW.ValueRO.OccupyingWorker != Entity.Null)
                return; // 이미 점유됨

            // 4. 가장 가까운 ResourceCenter 찾기
            Entity returnPointEntity = Entity.Null;
            if (rpc.ReturnPointGhostId == 0) // 자동 찾기
            {
                returnPointEntity = FindNearestResourceCenter(workerEntity, resourceCenters);
            }
            // TODO: rpc.ReturnPointGhostId != 0인 경우 해당 엔티티 사용

            // ResourceCenter가 없으면 채집 불가
            if (returnPointEntity == Entity.Null && resourceCenters.Length == 0)
                return;

            if (returnPointEntity == Entity.Null && resourceCenters.Length > 0)
            {
                returnPointEntity = resourceCenters[0]; // 일단 첫 번째 사용
            }

            // 5. 점유 설정
            nodeStateRW.ValueRW.OccupyingWorker = workerEntity;

            // 6. GatheringTarget 설정
            if (_gatheringTargetLookup.HasComponent(workerEntity))
            {
                RefRW<GatheringTarget> targetRW = _gatheringTargetLookup.GetRefRW(workerEntity);
                targetRW.ValueRW.ResourceNodeEntity = resourceNodeEntity;
                targetRW.ValueRW.ReturnPointEntity = returnPointEntity;
                targetRW.ValueRW.AutoReturn = true;
            }

            // 7. UnitState = MovingToGather
            if (_unitStateLookup.HasComponent(workerEntity))
            {
                RefRW<UnitState> stateRW = _unitStateLookup.GetRefRW(workerEntity);
                stateRW.ValueRW.CurrentState = UnitContext.MovingToGather;
            }

            // 8. PathfindingState 설정 (자원 노드 위치로 이동)
            if (_movementGoalLookup.HasComponent(workerEntity) &&
                _transformLookup.HasComponent(resourceNodeEntity))
            {
                float3 nodePosition = _transformLookup[resourceNodeEntity].Position;
                RefRW<MovementGoal> pathRW = _movementGoalLookup.GetRefRW(workerEntity);
                pathRW.ValueRW.Destination = nodePosition;
                pathRW.ValueRW.IsPathDirty = true;
            }
        }

        private Entity FindNearestResourceCenter(Entity workerEntity, NativeList<Entity> resourceCenters)
        {
            if (resourceCenters.Length == 0) return Entity.Null;
            if (!_transformLookup.HasComponent(workerEntity)) return resourceCenters[0];

            float3 workerPos = _transformLookup[workerEntity].Position;
            Entity nearest = Entity.Null;
            float minDist = float.MaxValue;

            for (int i = 0; i < resourceCenters.Length; i++)
            {
                Entity center = resourceCenters[i];
                if (!_transformLookup.HasComponent(center)) continue;

                float dist = math.distance(workerPos, _transformLookup[center].Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = center;
                }
            }

            return nearest;
        }
    }
}
