using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using UnityEngine;
using Shared;

namespace Server
{
    /// <summary>
    /// 건설 이동 명령 RPC 처리 시스템 (서버)
    /// - 빌더 검증 (GhostId → Entity, 소유권)
    /// - MovementGoal 설정
    /// - UnitIntentState.State = Intent.Build
    /// - PendingBuildServerData 추가 (도착 판정용)
    /// - MovementWaypoints 활성화
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleBuildMoveRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<BuilderTag> _builderTagLookup;
        [ReadOnly] private ComponentLookup<UnitTag> _unitTagLookup;

        private ComponentLookup<MovementGoal> _movementGoalLookup;
        private ComponentLookup<UnitIntentState> _unitIntentStateLookup;
        private ComponentLookup<AggroTarget> _aggroTargetLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GhostIdMap>();

            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _builderTagLookup = state.GetComponentLookup<BuilderTag>(true);
            _unitTagLookup = state.GetComponentLookup<UnitTag>(true);

            _movementGoalLookup = state.GetComponentLookup<MovementGoal>(false);
            _unitIntentStateLookup = state.GetComponentLookup<UnitIntentState>(false);
            _aggroTargetLookup = state.GetComponentLookup<AggroTarget>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _ghostOwnerLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _builderTagLookup.Update(ref state);
            _unitTagLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _aggroTargetLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostIdMap 싱글톤 재사용 (GhostIdLookupSystem이 매 프레임 갱신)
            var ghostMap = SystemAPI.GetSingleton<GhostIdMap>().Map;

            // RPC 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<BuildMoveRequestRpc>>()
                .WithEntityAccess())
            {
                if (ghostMap.TryGetValue(rpc.ValueRO.BuilderGhostId, out Entity builderEntity))
                {
                    ProcessRequest(
                        ecb,
                        builderEntity,
                        rpcReceive.ValueRO.SourceConnection,
                        rpc.ValueRO
                    );
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }

        private void ProcessRequest(
            EntityCommandBuffer ecb,
            Entity builderEntity,
            Entity sourceConnection,
            BuildMoveRequestRpc rpc)
        {
            // 1. 빌더 유효성 검증
            if (!_builderTagLookup.HasComponent(builderEntity) ||
                !_unitTagLookup.HasComponent(builderEntity) ||
                !_ghostOwnerLookup.HasComponent(builderEntity) ||
                !_networkIdLookup.HasComponent(sourceConnection))
            {
                return;
            }

            // 2. 소유권 검증
            int ownerId = _ghostOwnerLookup[builderEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;
            if (ownerId != requesterId)
            {
                return;
            }

            // 3. MovementGoal 설정
            if (_movementGoalLookup.HasComponent(builderEntity))
            {
                RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(builderEntity);
                goalRW.ValueRW.Destination = rpc.MoveTarget;
                goalRW.ValueRW.IsPathDirty = true;
                goalRW.ValueRW.CurrentWaypointIndex = 0;
            }

            // 4. UnitIntentState 설정 (Build)
            if (_unitIntentStateLookup.HasComponent(builderEntity))
            {
                RefRW<UnitIntentState> intentRW = _unitIntentStateLookup.GetRefRW(builderEntity);
                intentRW.ValueRW.State = Intent.Build;
                intentRW.ValueRW.TargetEntity = Entity.Null;
            }

            // 5. AggroTarget 초기화 (공격 대상 제거)
            if (_aggroTargetLookup.HasComponent(builderEntity))
            {
                RefRW<AggroTarget> aggroRW = _aggroTargetLookup.GetRefRW(builderEntity);
                aggroRW.ValueRW.TargetEntity = Entity.Null;
                aggroRW.ValueRW.LastTargetPosition = default;
            }

            // 6. MovementWaypoints 활성화
            ecb.SetComponentEnabled<MovementWaypoints>(builderEntity, true);

            // 7. PendingBuildServerData 추가 (기존 있으면 덮어쓰기)
            var pendingData = new PendingBuildServerData
            {
                StructureIndex = rpc.StructureIndex,
                GridPosition = rpc.GridPosition,
                BuildSiteCenter = rpc.BuildSiteCenter,
                StructureRadius = rpc.StructureRadius,
                OwnerNetworkId = requesterId,
                SourceConnection = sourceConnection
            };

            ecb.AddComponent(builderEntity, pendingData);
        }
    }
}
