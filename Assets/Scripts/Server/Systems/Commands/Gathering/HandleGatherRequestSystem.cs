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
    /// - Intent.Gather + Action.Moving + Phase.MovingToNode 설정
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
        [ReadOnly] private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;

        private ComponentLookup<ResourceNodeState> _resourceNodeStateLookup;
        private ComponentLookup<GatheringTarget> _gatheringTargetLookup;
        private ComponentLookup<UnitIntentState> _unitIntentStateLookup;
        private ComponentLookup<UnitActionState> _unitActionStateLookup;
        private ComponentLookup<WorkerState> _workerStateLookup;
        private ComponentLookup<MovementGoal> _movementGoalLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GhostIdMap>();

            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _workerTagLookup = state.GetComponentLookup<WorkerTag>(true);
            _resourceNodeTagLookup = state.GetComponentLookup<ResourceNodeTag>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);

            _resourceNodeStateLookup = state.GetComponentLookup<ResourceNodeState>(false);
            _gatheringTargetLookup = state.GetComponentLookup<GatheringTarget>(false);
            _unitIntentStateLookup = state.GetComponentLookup<UnitIntentState>(false);
            _unitActionStateLookup = state.GetComponentLookup<UnitActionState>(false);
            _workerStateLookup = state.GetComponentLookup<WorkerState>(false);
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
            _obstacleRadiusLookup.Update(ref state);
            _resourceNodeStateLookup.Update(ref state);
            _gatheringTargetLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _unitActionStateLookup.Update(ref state);
            _workerStateLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostIdMap 싱글톤 재사용 (GhostIdLookupSystem이 매 프레임 갱신)
            var ghostMap = SystemAPI.GetSingleton<GhostIdMap>().Map;

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

            // 3. 가장 가까운 ResourceCenter 찾기
            Entity returnPointEntity = Entity.Null;
            if (rpc.ReturnPointGhostId == 0) // 자동 찾기
            {
                returnPointEntity = FindNearestResourceCenter(workerEntity, resourceCenters);
            }
            // TODO: rpc.ReturnPointGhostId != 0인 경우 해당 엔티티 사용

            // 아군 ResourceCenter가 없으면 채집 불가
            if (returnPointEntity == Entity.Null)
                return;

            // 4. GatheringTarget 설정 (점유 여부와 무관하게 설정)
            if (_gatheringTargetLookup.HasComponent(workerEntity))
            {
                RefRW<GatheringTarget> targetRW = _gatheringTargetLookup.GetRefRW(workerEntity);
                targetRW.ValueRW.ResourceNodeEntity = resourceNodeEntity;
                targetRW.ValueRW.ReturnPointEntity = returnPointEntity;
                targetRW.ValueRW.AutoReturn = true;
            }

            // 5. 상태 설정: Intent.Gather + Action.Moving
            if (_unitIntentStateLookup.HasComponent(workerEntity))
            {
                RefRW<UnitIntentState> intentRW = _unitIntentStateLookup.GetRefRW(workerEntity);
                intentRW.ValueRW.State = Intent.Gather;
                intentRW.ValueRW.TargetEntity = resourceNodeEntity;
            }

            if (_unitActionStateLookup.HasComponent(workerEntity))
            {
                RefRW<UnitActionState> actionRW = _unitActionStateLookup.GetRefRW(workerEntity);
                actionRW.ValueRW.State = Action.Moving;
            }

            // 6. 점유 상태에 따른 Phase 분기
            RefRW<ResourceNodeState> nodeStateRW = _resourceNodeStateLookup.GetRefRW(resourceNodeEntity);
            Entity currentOccupier = nodeStateRW.ValueRO.OccupyingWorker;

            if (_workerStateLookup.HasComponent(workerEntity))
            {
                RefRW<WorkerState> workerStateRW = _workerStateLookup.GetRefRW(workerEntity);

                if (currentOccupier == Entity.Null || currentOccupier == workerEntity)
                {
                    // 비점유 또는 자기 자신이 점유 중: 즉시 점유 설정 + MovingToNode
                    nodeStateRW.ValueRW.OccupyingWorker = workerEntity;
                    workerStateRW.ValueRW.Phase = GatherPhase.MovingToNode;
                }
                else
                {
                    // 다른 워커가 점유 중: WaitingForNode (점유 없이 이동 후 대기)
                    workerStateRW.ValueRW.Phase = GatherPhase.WaitingForNode;
                }
            }

            // 7. PathfindingState 설정 (자원 노드 표면으로 이동)
            if (_movementGoalLookup.HasComponent(workerEntity) &&
                _transformLookup.HasComponent(resourceNodeEntity))
            {
                float3 workerPos = _transformLookup[workerEntity].Position;
                float3 nodePos = _transformLookup[resourceNodeEntity].Position;
                float3 targetPos = CalculateNodeTargetPosition(workerPos, nodePos, resourceNodeEntity, workerEntity);

                RefRW<MovementGoal> pathRW = _movementGoalLookup.GetRefRW(workerEntity);
                pathRW.ValueRW.Destination = targetPos;
                pathRW.ValueRW.IsPathDirty = true;

                // 이동 활성화
                ecb.SetComponentEnabled<MovementWaypoints>(workerEntity, true);
            }
        }

        private Entity FindNearestResourceCenter(Entity workerEntity, NativeList<Entity> resourceCenters)
        {
            if (resourceCenters.Length == 0) return Entity.Null;
            if (!_transformLookup.HasComponent(workerEntity)) return Entity.Null;
            if (!_ghostOwnerLookup.HasComponent(workerEntity)) return Entity.Null;

            int workerOwnerId = _ghostOwnerLookup[workerEntity].NetworkId;
            float3 workerPos = _transformLookup[workerEntity].Position;
            Entity nearest = Entity.Null;
            float minDist = float.MaxValue;

            for (int i = 0; i < resourceCenters.Length; i++)
            {
                Entity center = resourceCenters[i];
                if (!_transformLookup.HasComponent(center)) continue;
                if (!_ghostOwnerLookup.HasComponent(center)) continue;
                if (_ghostOwnerLookup[center].NetworkId != workerOwnerId) continue;

                float dist = math.distance(workerPos, _transformLookup[center].Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = center;
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
            float nodeRadius = _obstacleRadiusLookup.HasComponent(nodeEntity)
                ? _obstacleRadiusLookup[nodeEntity].Radius
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
    }
}
