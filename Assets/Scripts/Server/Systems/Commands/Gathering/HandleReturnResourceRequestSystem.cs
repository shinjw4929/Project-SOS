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
    /// 자원 반납 요청 RPC 처리 시스템 (서버)
    /// - 자원을 들고 있는 Worker가 ResourceCenter를 우클릭했을 때
    /// - Phase.MovingToReturn 설정 → 반납 완료 후 LastGatheredNodeEntity로 자동 복귀
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleReturnResourceRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<WorkerTag> _workerTagLookup;
        [ReadOnly] private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;

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
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);

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
            _resourceCenterTagLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);
            _gatheringTargetLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _unitActionStateLookup.Update(ref state);
            _workerStateLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostIdMap 싱글톤 재사용 (GhostIdLookupSystem이 매 프레임 갱신)
            var ghostMap = SystemAPI.GetSingleton<GhostIdMap>().Map;

            // RPC 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ReturnResourceRequestRpc>>()
                .WithEntityAccess())
            {
                if (ghostMap.TryGetValue(rpc.ValueRO.WorkerGhostId, out Entity workerEntity) &&
                    ghostMap.TryGetValue(rpc.ValueRO.ResourceCenterGhostId, out Entity resourceCenterEntity))
                {
                    ProcessRequest(ecb, workerEntity, resourceCenterEntity, rpcReceive.ValueRO.SourceConnection);
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }

        private void ProcessRequest(
            EntityCommandBuffer ecb,
            Entity workerEntity,
            Entity resourceCenterEntity,
            Entity sourceConnection)
        {
            // 1. Worker 유효성 검증
            if (!_workerTagLookup.HasComponent(workerEntity) ||
                !_ghostOwnerLookup.HasComponent(workerEntity) ||
                !_networkIdLookup.HasComponent(sourceConnection))
                return;

            int ownerId = _ghostOwnerLookup[workerEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;
            if (ownerId != requesterId) return;

            // 2. ResourceCenter 유효성 검증
            if (!_resourceCenterTagLookup.HasComponent(resourceCenterEntity))
                return;

            // 3. ResourceCenter 소유권 검증 (다른 유저의 센터에 반납 방지)
            if (!_ghostOwnerLookup.HasComponent(resourceCenterEntity))
                return;
            if (_ghostOwnerLookup[resourceCenterEntity].NetworkId != ownerId)
                return;

            // 4. 자원을 들고 있는지 확인
            if (!_workerStateLookup.HasComponent(workerEntity))
                return;

            RefRW<WorkerState> workerStateRW = _workerStateLookup.GetRefRW(workerEntity);
            if (workerStateRW.ValueRO.CarriedAmount <= 0)
                return; // 자원이 없으면 반납할 것도 없음

            // 5. GatheringTarget 업데이트
            if (_gatheringTargetLookup.HasComponent(workerEntity))
            {
                RefRW<GatheringTarget> targetRW = _gatheringTargetLookup.GetRefRW(workerEntity);

                // 현재 ResourceNodeEntity를 LastGatheredNodeEntity로 저장 (없으면 기존 값 유지)
                if (targetRW.ValueRO.ResourceNodeEntity != Entity.Null)
                {
                    targetRW.ValueRW.LastGatheredNodeEntity = targetRW.ValueRO.ResourceNodeEntity;
                }

                // 반납 지점 설정
                targetRW.ValueRW.ReturnPointEntity = resourceCenterEntity;
                targetRW.ValueRW.AutoReturn = true;
            }

            // 6. 상태 설정: Intent.Gather + Action.Moving + Phase.MovingToReturn
            if (_unitIntentStateLookup.HasComponent(workerEntity))
            {
                RefRW<UnitIntentState> intentRW = _unitIntentStateLookup.GetRefRW(workerEntity);
                intentRW.ValueRW.State = Intent.Gather;
                intentRW.ValueRW.TargetEntity = resourceCenterEntity;
            }

            if (_unitActionStateLookup.HasComponent(workerEntity))
            {
                RefRW<UnitActionState> actionRW = _unitActionStateLookup.GetRefRW(workerEntity);
                actionRW.ValueRW.State = Action.Moving;
            }

            // Phase를 MovingToReturn으로 설정 (채굴 없이 바로 반납)
            workerStateRW.ValueRW.Phase = GatherPhase.MovingToReturn;

            // 7. MovementGoal 설정 (ResourceCenter 표면으로 이동)
            if (_movementGoalLookup.HasComponent(workerEntity) &&
                _transformLookup.HasComponent(resourceCenterEntity))
            {
                float3 workerPos = _transformLookup[workerEntity].Position;
                float3 centerPos = _transformLookup[resourceCenterEntity].Position;
                float3 targetPos = CalculateCenterTargetPosition(workerPos, centerPos, resourceCenterEntity, workerEntity);

                RefRW<MovementGoal> pathRW = _movementGoalLookup.GetRefRW(workerEntity);
                pathRW.ValueRW.Destination = targetPos;
                pathRW.ValueRW.IsPathDirty = true;

                // 이동 활성화
                ecb.SetComponentEnabled<MovementWaypoints>(workerEntity, true);
            }
        }

        /// <summary>
        /// 워커 → ResourceCenter 직선 상의 표면 지점 계산
        /// </summary>
        private float3 CalculateCenterTargetPosition(float3 workerPos, float3 centerPos, Entity centerEntity, Entity workerEntity)
        {
            // 워커 → 센터 방향 벡터
            float3 direction = centerPos - workerPos;
            float len = math.length(direction);

            if (len < 0.001f)
            {
                return centerPos;
            }

            direction = direction / len;

            // 센터 반지름
            float centerRadius = _obstacleRadiusLookup.HasComponent(centerEntity)
                ? _obstacleRadiusLookup[centerEntity].Radius
                : 1.5f;

            // 유닛 반지름
            float unitRadius = _obstacleRadiusLookup.HasComponent(workerEntity)
                ? _obstacleRadiusLookup[workerEntity].Radius
                : 0.5f;

            // 센터 표면 지점
            float offset = centerRadius + unitRadius + 0.1f;
            float3 targetPos = centerPos - direction * offset;

            return targetPos;
        }
    }
}
