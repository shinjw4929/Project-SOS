using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using UnityEngine;
using Shared;

namespace Server
{
    /// <summary>
    /// 이동 명령 RPC 처리 시스템 (서버)
    /// - 소유권 검증
    /// - MovementGoal 설정
    /// - UnitIntentState.State = Intent.Move
    /// - AggroTarget 초기화
    /// - MovementWaypoints 활성화
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleMoveRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
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
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<MoveRequestRpc>>()
                .WithEntityAccess())
            {
                if (ghostMap.TryGetValue(rpc.ValueRO.UnitGhostId, out Entity unitEntity))
                {
                    ProcessRequest(
                        ecb,
                        unitEntity,
                        rpcReceive.ValueRO.SourceConnection,
                        rpc.ValueRO
                    );
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }

        private void ProcessRequest(
            EntityCommandBuffer ecb,
            Entity unitEntity,
            Entity sourceConnection,
            MoveRequestRpc rpc)
        {
            // 1. 유닛 유효성 검증
            if (!_unitTagLookup.HasComponent(unitEntity) ||
                !_ghostOwnerLookup.HasComponent(unitEntity) ||
                !_networkIdLookup.HasComponent(sourceConnection))
            {
                Debug.LogWarning($"[HandleMoveRequest] Validation FAILED: UnitTag={_unitTagLookup.HasComponent(unitEntity)}, GhostOwner={_ghostOwnerLookup.HasComponent(unitEntity)}, NetworkId={_networkIdLookup.HasComponent(sourceConnection)}");
                return;
            }

            // 2. 소유권 검증
            int ownerId = _ghostOwnerLookup[unitEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;
            if (ownerId != requesterId)
            {
                Debug.LogWarning($"[HandleMoveRequest] Owner mismatch: ownerId={ownerId}, requesterId={requesterId}");
                return;
            }

            // 3. MovementGoal 설정
            if (_movementGoalLookup.HasComponent(unitEntity))
            {
                RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(unitEntity);
                goalRW.ValueRW.Destination = rpc.TargetPosition;
                goalRW.ValueRW.IsPathDirty = true;
                goalRW.ValueRW.CurrentWaypointIndex = 0;
            }
            else
            {
                Debug.LogWarning($"[HandleMoveRequest] No MovementGoal component!");
            }

            // 4. UnitIntentState 설정 (Move)
            if (_unitIntentStateLookup.HasComponent(unitEntity))
            {
                RefRW<UnitIntentState> intentRW = _unitIntentStateLookup.GetRefRW(unitEntity);
                intentRW.ValueRW.State = Intent.Move;
                intentRW.ValueRW.TargetEntity = Entity.Null;
            }

            // 5. AggroTarget 초기화 (공격 대상 제거)
            if (_aggroTargetLookup.HasComponent(unitEntity))
            {
                RefRW<AggroTarget> aggroRW = _aggroTargetLookup.GetRefRW(unitEntity);
                aggroRW.ValueRW.TargetEntity = Entity.Null;
                aggroRW.ValueRW.LastTargetPosition = default;
            }

            // 6. MovementWaypoints 활성화
            ecb.SetComponentEnabled<MovementWaypoints>(unitEntity, true);
        }
    }
}
