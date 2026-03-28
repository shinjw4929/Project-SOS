using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 이동 명령 RPC 처리 시스템 (서버)
    /// - 소유권 검증
    /// - MovementGoal 설정 + 동일 목적지 유닛 격자 대형 오프셋
    /// - UnitIntentState.State = Intent.Move + UnitActionState.State = Action.Moving
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
        private ComponentLookup<UnitActionState> _unitActionStateLookup;
        private ComponentLookup<AggroTarget> _aggroTargetLookup;
        private ComponentLookup<AggroLock> _aggroLockLookup;
        [ReadOnly] private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;

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
            _unitActionStateLookup = state.GetComponentLookup<UnitActionState>(false);
            _aggroTargetLookup = state.GetComponentLookup<AggroTarget>(false);
            _aggroLockLookup = state.GetComponentLookup<AggroLock>(false);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _ghostOwnerLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _unitTagLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _unitActionStateLookup.Update(ref state);
            _aggroTargetLookup.Update(ref state);
            _aggroLockLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);
            _transformLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostIdMap 싱글톤 재사용 (GhostIdLookupSystem이 매 프레임 갱신)
            var ghostMap = SystemAPI.GetSingleton<GhostIdMap>().Map;

            // === Pass 1: RPC 수집 + 개별 처리 ===
            var groupBuffer = new NativeList<GroupMoveEntry>(16, Allocator.Temp);

            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<MoveRequestRpc>>()
                .WithEntityAccess())
            {
                if (ghostMap.TryGetValue(rpc.ValueRO.UnitGhostId, out Entity unitEntity))
                {
                    ProcessRequest(ecb, unitEntity, rpcReceive.ValueRO.SourceConnection, rpc.ValueRO);

                    int ownerId = _ghostOwnerLookup.HasComponent(unitEntity)
                        ? _ghostOwnerLookup[unitEntity].NetworkId : -1;

                    groupBuffer.Add(new GroupMoveEntry
                    {
                        UnitEntity = unitEntity,
                        TargetPosition = rpc.ValueRO.TargetPosition,
                        OwnerId = ownerId
                    });
                }

                ecb.DestroyEntity(rpcEntity);
            }

            // === Pass 2: 동일 소유자 + 동일 목적지 그룹핑 → 오프셋 적용 ===
            ApplyFormationOffsets(groupBuffer);
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
                return;

            // 2. 소유권 검증
            int ownerId = _ghostOwnerLookup[unitEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;
            if (ownerId != requesterId)
                return;

            // 3. MovementGoal 설정
            if (_movementGoalLookup.HasComponent(unitEntity))
            {
                RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(unitEntity);
                goalRW.ValueRW.Destination = rpc.TargetPosition;
                goalRW.ValueRW.IsPathDirty = true;
            }

            // 4. UnitIntentState 설정 (Move 또는 AttackMove)
            if (_unitIntentStateLookup.HasComponent(unitEntity))
            {
                RefRW<UnitIntentState> intentRW = _unitIntentStateLookup.GetRefRW(unitEntity);
                intentRW.ValueRW.State = rpc.IsAttackMove ? Intent.AttackMove : Intent.Move;
                intentRW.ValueRW.TargetEntity = Entity.Null;
            }

            // 4-1. UnitActionState → Moving (걷기 애니메이션 트리거)
            if (_unitActionStateLookup.HasComponent(unitEntity))
            {
                _unitActionStateLookup.GetRefRW(unitEntity).ValueRW.State = Action.Moving;
            }

            // 5. AggroTarget + AggroLock 초기화 (공격 대상 및 어그로 고정 해제)
            if (_aggroTargetLookup.HasComponent(unitEntity))
            {
                RefRW<AggroTarget> aggroRW = _aggroTargetLookup.GetRefRW(unitEntity);
                aggroRW.ValueRW.TargetEntity = Entity.Null;
                aggroRW.ValueRW.LastTargetPosition = default;
            }

            if (_aggroLockLookup.HasComponent(unitEntity))
            {
                RefRW<AggroLock> lockRW = _aggroLockLookup.GetRefRW(unitEntity);
                lockRW.ValueRW.LockedTarget = Entity.Null;
                lockRW.ValueRW.RemainingLockTime = 0f;
            }

            // 6. MovementWaypoints 활성화
            ecb.SetComponentEnabled<MovementWaypoints>(unitEntity, true);
        }

        private struct GroupMoveEntry
        {
            public Entity UnitEntity;
            public float3 TargetPosition;
            public int OwnerId;
        }

        /// <summary>
        /// 동일 소유자 + 동일 목적지(1m 이내) 유닛을 그룹으로 묶어 격자 오프셋 적용.
        /// 2개 이상의 유닛이 같은 지점으로 이동할 때만 오프셋 발생.
        /// </summary>
        private void ApplyFormationOffsets(NativeList<GroupMoveEntry> entries)
        {
            if (entries.Length <= 1) return;

            // 그룹핑: 동일 소유자 + 목적지 1m 이내를 같은 그룹으로
            var groupIds = new NativeArray<int>(entries.Length, Allocator.Temp);
            int nextGroupId = 0;
            for (int i = 0; i < entries.Length; i++) groupIds[i] = -1;

            for (int i = 0; i < entries.Length; i++)
            {
                if (groupIds[i] >= 0) continue;
                groupIds[i] = nextGroupId;

                for (int j = i + 1; j < entries.Length; j++)
                {
                    if (groupIds[j] >= 0) continue;
                    if (entries[i].OwnerId != entries[j].OwnerId) continue;
                    float3 diff = entries[i].TargetPosition - entries[j].TargetPosition;
                    diff.y = 0;
                    if (math.lengthsq(diff) > 1f) continue;
                    groupIds[j] = nextGroupId;
                }
                nextGroupId++;
            }

            // 각 그룹별 오프셋 적용
            for (int gid = 0; gid < nextGroupId; gid++)
            {
                // 그룹 멤버 수집
                int count = 0;
                float3 groupCenter = float3.zero;
                float3 groupDest = float3.zero;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (groupIds[i] != gid) continue;
                    groupDest = entries[i].TargetPosition;
                    if (_transformLookup.HasComponent(entries[i].UnitEntity))
                        groupCenter += _transformLookup[entries[i].UnitEntity].Position;
                    count++;
                }

                if (count <= 1) continue;

                groupCenter /= count;
                float3 moveDir = math.normalizesafe(groupDest - groupCenter);
                if (math.lengthsq(moveDir) < 0.001f) moveDir = new float3(0, 0, 1);

                // spacing 결정: 그룹 내 최대 ObstacleRadius * 2.5 (큰 유닛 기준으로 간격 확보)
                float maxRadius = 0.6f;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (groupIds[i] != gid) continue;
                    if (_obstacleRadiusLookup.TryGetComponent(entries[i].UnitEntity, out ObstacleRadius r))
                        maxRadius = math.max(maxRadius, r.Radius);
                }
                float spacing = math.max(maxRadius * 2.5f, 1.5f);

                // 슬롯 인덱스 부여 + 오프셋 적용
                int slotIndex = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (groupIds[i] != gid) continue;

                    float3 offset = FormationUtility.CalculateFormationOffset(slotIndex, count, spacing, moveDir);

                    if (_movementGoalLookup.HasComponent(entries[i].UnitEntity))
                    {
                        RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(entries[i].UnitEntity);
                        goalRW.ValueRW.Destination = groupDest + offset;
                    }
                    slotIndex++;
                }
            }
        }
    }
}
