using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using Unity.Transforms;
using UnityEngine;
using Shared;

namespace Server
{
    /// <summary>
    /// 건설 이동 명령 RPC 처리 시스템 (서버)
    /// - 빌더 검증 (GhostId → Entity, 소유권)
    /// - MovementGoal 설정 (Destination = BuildSiteCenter)
    /// - BuildApproachRadius 추가 (AABB 표면 기준 조기 정지)
    /// - UnitIntentState.State = Intent.Build
    /// - PendingBuildServerData 추가 (도착 판정용)
    /// - MovementWaypoints 활성화
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UnifiedTargetingSystem))] // MovementGoal Job 의존성 해결
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleBuildMoveRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<BuilderTag> _builderTagLookup;
        [ReadOnly] private ComponentLookup<UnitTag> _unitTagLookup;
        [ReadOnly] private ComponentLookup<WorkRange> _workRangeLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
        [ReadOnly] private ComponentLookup<UnitActionState> _unitActionStateLookup;

        private ComponentLookup<MovementGoal> _movementGoalLookup;
        private ComponentLookup<MovementWaypoints> _movementWaypointsLookup;
        private ComponentLookup<UnitIntentState> _unitIntentStateLookup;
        private ComponentLookup<AggroTarget> _aggroTargetLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GhostIdMap>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<GridSettings>();

            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _builderTagLookup = state.GetComponentLookup<BuilderTag>(true);
            _unitTagLookup = state.GetComponentLookup<UnitTag>(true);
            _workRangeLookup = state.GetComponentLookup<WorkRange>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _unitActionStateLookup = state.GetComponentLookup<UnitActionState>(true);

            _movementGoalLookup = state.GetComponentLookup<MovementGoal>(false);
            _movementWaypointsLookup = state.GetComponentLookup<MovementWaypoints>(false);
            _unitIntentStateLookup = state.GetComponentLookup<UnitIntentState>(false);
            _aggroTargetLookup = state.GetComponentLookup<AggroTarget>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // UnifiedTargetingSystem의 MovementGoal Job이 완료될 때까지 대기
            state.CompleteDependency();

            _ghostOwnerLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _builderTagLookup.Update(ref state);
            _unitTagLookup.Update(ref state);
            _workRangeLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _footprintLookup.Update(ref state);
            _unitActionStateLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);
            _movementWaypointsLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);
            _aggroTargetLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // GhostIdMap 싱글톤 재사용 (GhostIdLookupSystem이 매 프레임 갱신)
            var ghostMap = SystemAPI.GetSingleton<GhostIdMap>().Map;

            float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var catalogEntity = SystemAPI.GetSingletonEntity<StructureCatalog>();
            var prefabBuffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity).AsNativeArray();

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
                        rpc.ValueRO,
                        elapsedTime,
                        prefabBuffer,
                        gridSettings
                    );
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }

        private void ProcessRequest(
            EntityCommandBuffer ecb,
            Entity builderEntity,
            Entity sourceConnection,
            BuildMoveRequestRpc rpc,
            float elapsedTime,
            NativeArray<StructureCatalogElement> prefabBuffer,
            GridSettings gridSettings)
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

            // 2-1. Dying/Dead 유닛은 명령 무시
            if (_unitActionStateLookup.TryGetComponent(builderEntity, out var builderAction) &&
                (builderAction.State == Action.Dying || builderAction.State == Action.Dead))
                return;

            // 3. MovementGoal 설정: BuildSiteCenter로 이동 + AABB 표면 기준 조기 정지
            float workRange = _workRangeLookup.TryGetComponent(builderEntity, out var wr)
                ? wr.Value : 1.0f;

            if (_movementGoalLookup.HasComponent(builderEntity) &&
                _transformLookup.HasComponent(builderEntity))
            {
                RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(builderEntity);

                // 카탈로그에서 Footprint 조회
                float halfW = gridSettings.CellSize * 0.5f;
                float halfL = gridSettings.CellSize * 0.5f;
                if (rpc.StructureIndex >= 0 && rpc.StructureIndex < prefabBuffer.Length)
                {
                    Entity prefab = prefabBuffer[rpc.StructureIndex].PrefabEntity;
                    if (_footprintLookup.TryGetComponent(prefab, out var footprint))
                    {
                        halfW = footprint.Width * gridSettings.CellSize * 0.5f;
                        halfL = footprint.Length * gridSettings.CellSize * 0.5f;
                    }
                }

                goalRW.ValueRW.Destination = rpc.BuildSiteCenter;
                goalRW.ValueRW.IsPathDirty = true;
                goalRW.ValueRW.DestinationSetTime = elapsedTime;

                ecb.AddComponent(builderEntity, new BuildApproachRadius
                {
                    Value = workRange,
                    Center = rpc.BuildSiteCenter,
                    HalfW = halfW,
                    HalfL = halfL
                });

                FixedString128Bytes moveMsg = "BuildMove";
                GameLogger.Pos(ref moveMsg, "builder", _transformLookup[builderEntity].Position);
                GameLogger.Pos(ref moveMsg, "site", rpc.BuildSiteCenter);
                GameLogger.Field(ref moveMsg, "stopR", (int)(workRange * 100));
                GameLogger.Info(LogWorld.Server, LogCategory.Construction, in moveMsg);
            }

            // ArrivalRadius 설정 (Dead Zone 방지: ArrivalRadius * 2 <= workRange)
            if (_movementWaypointsLookup.HasComponent(builderEntity))
            {
                _movementWaypointsLookup.GetRefRW(builderEntity).ValueRW.ArrivalRadius =
                    ArrivalUtility.GetSafeArrivalRadius(workRange, 0f);
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
