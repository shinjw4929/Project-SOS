using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using Shared;

namespace Server
{
    /// <summary>
    /// 공격 명령 RPC 처리 시스템 (서버)
    /// - 소유권 검증
    /// - 타겟 검증 (EnemyTag 확인)
    /// - UnitIntentState.State = Intent.Attack
    /// - AggroTarget.TargetEntity 설정
    /// - MovementGoal.Destination = 타겟 위치 (추격)
    /// - MovementWaypoints 활성화
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleAttackRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<UnitTag> _unitTagLookup;
        [ReadOnly] private ComponentLookup<EnemyTag> _enemyTagLookup;

        private ComponentLookup<MovementGoal> _movementGoalLookup;
        private ComponentLookup<UnitIntentState> _unitIntentStateLookup;
        private ComponentLookup<AggroTarget> _aggroTargetLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _unitTagLookup = state.GetComponentLookup<UnitTag>(true);
            _enemyTagLookup = state.GetComponentLookup<EnemyTag>(true);

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
            _enemyTagLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _aggroTargetLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostMap 생성
            var ghostMap = new NativeParallelHashMap<int, Entity>(1024, Allocator.Temp);
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithEntityAccess())
            {
                ghostMap.TryAdd(ghost.ValueRO.ghostId, entity);
            }

            // RPC 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<AttackRequestRpc>>()
                .WithEntityAccess())
            {
                if (ghostMap.TryGetValue(rpc.ValueRO.UnitGhostId, out Entity unitEntity))
                {
                    // 타겟도 GhostMap에서 찾기
                    Entity targetEntity = Entity.Null;
                    if (rpc.ValueRO.TargetGhostId != 0)
                    {
                        ghostMap.TryGetValue(rpc.ValueRO.TargetGhostId, out targetEntity);
                    }

                    ProcessRequest(
                        ecb,
                        unitEntity,
                        targetEntity,
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
            Entity targetEntity,
            Entity sourceConnection,
            AttackRequestRpc rpc)
        {
            // 1. 유닛 유효성 검증
            if (!_unitTagLookup.HasComponent(unitEntity) ||
                !_ghostOwnerLookup.HasComponent(unitEntity) ||
                !_networkIdLookup.HasComponent(sourceConnection))
                return;

            // 2. 소유권 검증
            int ownerId = _ghostOwnerLookup[unitEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;
            if (ownerId != requesterId) return;

            // 3. 타겟 검증 (EnemyTag 확인)
            if (targetEntity == Entity.Null || !_enemyTagLookup.HasComponent(targetEntity))
                return;

            // 4. UnitIntentState 설정 (Attack)
            if (_unitIntentStateLookup.HasComponent(unitEntity))
            {
                RefRW<UnitIntentState> intentRW = _unitIntentStateLookup.GetRefRW(unitEntity);
                intentRW.ValueRW.State = Intent.Attack;
                intentRW.ValueRW.TargetEntity = targetEntity;
            }

            // 5. AggroTarget 설정
            if (_aggroTargetLookup.HasComponent(unitEntity))
            {
                RefRW<AggroTarget> aggroRW = _aggroTargetLookup.GetRefRW(unitEntity);
                aggroRW.ValueRW.TargetEntity = targetEntity;
                aggroRW.ValueRW.LastTargetPosition = rpc.TargetPosition;
            }

            // 6. MovementGoal 설정 (타겟 위치로 추격)
            if (_movementGoalLookup.HasComponent(unitEntity))
            {
                RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(unitEntity);
                goalRW.ValueRW.Destination = rpc.TargetPosition;
                goalRW.ValueRW.IsPathDirty = true;
                goalRW.ValueRW.CurrentWaypointIndex = 0;
            }

            // 7. MovementWaypoints 활성화
            ecb.SetComponentEnabled<MovementWaypoints>(unitEntity, true);
        }
    }
}
