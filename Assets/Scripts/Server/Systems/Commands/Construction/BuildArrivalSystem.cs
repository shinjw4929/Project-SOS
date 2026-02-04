using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Collections;
using Unity.Physics;
using Shared;

namespace Server
{
    /// <summary>
    /// 건설 도착 시스템
    /// - PendingBuildServerData가 있고 MovementWaypoints가 비활성화된 유닛 감지
    /// - 건물 생성 (HandleBuildRequestSystem 로직 재사용)
    /// - PendingBuildServerData 제거
    /// - UnitIntentState를 Idle로 복원
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementArrivalSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct BuildArrivalSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<ProductionInfo> _productionInfoLookup;
        [ReadOnly] private ComponentLookup<NeedsNavMeshObstacle> _needsNavMeshLookup;
        [ReadOnly] private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        [ReadOnly] private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;
        [ReadOnly] private ComponentLookup<WorkRange> _workRangeLookup;
        [ReadOnly] private ComponentLookup<PhysicsVelocity> _velocityLookup;

        private ComponentLookup<UserCurrency> _userCurrencyLookup;
        private ComponentLookup<UserTechState> _userTechStateLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);
            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _productionInfoLookup = state.GetComponentLookup<ProductionInfo>(true);
            _needsNavMeshLookup = state.GetComponentLookup<NeedsNavMeshObstacle>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
            _workRangeLookup = state.GetComponentLookup<WorkRange>(true);
            _velocityLookup = state.GetComponentLookup<PhysicsVelocity>(true);

            _userCurrencyLookup = state.GetComponentLookup<UserCurrency>(false);
            _userTechStateLookup = state.GetComponentLookup<UserTechState>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<StructureCatalog>(out var catalogEntity))
                return;

            _productionCostLookup.Update(ref state);
            _footprintLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _productionInfoLookup.Update(ref state);
            _needsNavMeshLookup.Update(ref state);
            _resourceCenterTagLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);
            _workRangeLookup.Update(ref state);
            _velocityLookup.Update(ref state);
            _userCurrencyLookup.Update(ref state);
            _userTechStateLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var prefabBuffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity).AsNativeArray();

            // NetworkId → UserCurrency 매핑
            var networkIdToCurrencyMap = new NativeHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                         .WithAll<UserEconomyTag>()
                         .WithEntityAccess())
            {
                networkIdToCurrencyMap.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
            }

            // PendingBuildServerData가 있고 MovementWaypoints가 비활성화된 유닛 감지
            // IgnoreComponentEnabledState 사용하여 비활성화된 MovementWaypoints도 쿼리
            foreach (var (pendingData, transform, intentState, waypoints, waypointsEnabled, entity) in
                     SystemAPI.Query<
                         RefRO<PendingBuildServerData>,
                         RefRO<LocalTransform>,
                         RefRW<UnitIntentState>,
                         RefRW<MovementWaypoints>,
                         EnabledRefRO<MovementWaypoints>>()
                         .WithAll<BuilderTag>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                var pending = pendingData.ValueRO;

                // 거리 계산 (BuildSiteCenter 기준)
                float3 unitPos = transform.ValueRO.Position;
                float centerDistance = math.distance(
                    new float2(unitPos.x, unitPos.z),
                    new float2(pending.BuildSiteCenter.x, pending.BuildSiteCenter.z)
                );
                float distanceToSurface = centerDistance - pending.StructureRadius;

                float workRange = 1.0f;
                if (_workRangeLookup.HasComponent(entity))
                {
                    workRange = _workRangeLookup[entity].Value;
                }
                float arrivalThreshold = workRange + 1.0f;

                // 도착 판정: MovementWaypoints 비활성화 OR (사거리 내 + 저속)
                bool waypointsDone = !waypointsEnabled.ValueRO;
                bool inRangeAndStopped = false;

                if (!waypointsDone && distanceToSurface <= arrivalThreshold)
                {
                    // MovementWaypoints가 아직 enabled이지만 사거리 내에서 거의 멈춤
                    // (ECB 타이밍 이슈로 disabled가 안 된 경우 백업)
                    float speed = _velocityLookup.HasComponent(entity)
                        ? math.length(_velocityLookup[entity].Linear)
                        : 0f;
                    inRangeAndStopped = speed < 0.1f;
                }

                if (!waypointsDone && !inRangeAndStopped)
                    continue;

                if (distanceToSurface > arrivalThreshold)
                    continue;

                // 건물 생성 시도
                bool buildSuccess = TryBuild(
                    ecb,
                    pending,
                    prefabBuffer,
                    gridSettings,
                    networkIdToCurrencyMap
                );

                // PendingBuildServerData 제거
                ecb.RemoveComponent<PendingBuildServerData>(entity);

                // ArrivalRadius 초기화 + MovementWaypoints 비활성화
                waypoints.ValueRW.ArrivalRadius = 0f;
                ecb.SetComponentEnabled<MovementWaypoints>(entity, false);

                // UnitIntentState를 Idle로 복원
                intentState.ValueRW.State = Intent.Idle;
                intentState.ValueRW.TargetEntity = Entity.Null;
            }
        }

        private bool TryBuild(
            EntityCommandBuffer ecb,
            PendingBuildServerData pending,
            NativeArray<StructureCatalogElement> prefabBuffer,
            GridSettings gridSettings,
            NativeHashMap<int, Entity> networkIdToCurrencyMap)
        {
            // 1. 프리팹 Index 검증
            if (pending.StructureIndex < 0 || pending.StructureIndex >= prefabBuffer.Length)
                return false;

            Entity structurePrefab = prefabBuffer[pending.StructureIndex].PrefabEntity;
            if (structurePrefab == Entity.Null || !_footprintLookup.HasComponent(structurePrefab))
                return false;

            // 2. 유저 자원 엔티티 확인
            if (!networkIdToCurrencyMap.TryGetValue(pending.OwnerNetworkId, out Entity userCurrencyEntity))
                return false;

            // 3. 자원 확인 및 차감
            if (!_userCurrencyLookup.HasComponent(userCurrencyEntity))
                return false;

            var currency = _userCurrencyLookup[userCurrencyEntity];
            int structureCost = _productionCostLookup[structurePrefab].Cost;

            if (currency.Amount < structureCost)
            {
                // 자원 부족 알림 RPC 전송
                if (pending.SourceConnection != Entity.Null)
                {
                    var notifyEntity = ecb.CreateEntity();
                    ecb.AddComponent(notifyEntity, new NotificationRpc { Type = NotificationType.InsufficientFunds });
                    ecb.AddComponent(notifyEntity, new SendRpcCommandRequest { TargetConnection = pending.SourceConnection });
                }
                return false;
            }

            currency.Amount -= structureCost;
            _userCurrencyLookup[userCurrencyEntity] = currency;

            // 4. 건물 생성
            var footprint = _footprintLookup[structurePrefab];
            float3 buildingCenter = GridUtility.GridToWorld(
                pending.GridPosition.x,
                pending.GridPosition.y,
                footprint.Width,
                footprint.Length,
                gridSettings
            );
            buildingCenter.y += footprint.Height * 0.5f;

            Entity newStructure = ecb.Instantiate(structurePrefab);

            if (_transformLookup.HasComponent(structurePrefab))
            {
                var transform = _transformLookup[structurePrefab];
                transform.Position = buildingCenter;
                ecb.SetComponent(newStructure, transform);
            }
            else
            {
                ecb.SetComponent(newStructure, LocalTransform.FromPosition(buildingCenter));
            }

            ecb.SetComponent(newStructure, new GridPosition { Position = pending.GridPosition });
            ecb.AddComponent(newStructure, new GhostOwner { NetworkId = pending.OwnerNetworkId });
            ecb.SetComponent(newStructure, new Team { teamId = pending.OwnerNetworkId });

            if (_productionInfoLookup.HasComponent(structurePrefab))
            {
                var info = _productionInfoLookup[structurePrefab];
                ecb.AddComponent(newStructure, new UnderConstructionTag
                {
                    Progress = 0f,
                    TotalBuildTime = info.ProductionTime
                });
            }

            if (_needsNavMeshLookup.HasComponent(structurePrefab))
            {
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(newStructure, true);
            }

            // 5. ResourceCenter 건설 시 테크 상태 업데이트
            if (_resourceCenterTagLookup.HasComponent(structurePrefab))
            {
                if (_userTechStateLookup.HasComponent(userCurrencyEntity))
                {
                    var techState = _userTechStateLookup[userCurrencyEntity];
                    techState.HasResourceCenter = true;
                    _userTechStateLookup[userCurrencyEntity] = techState;
                }
            }

            return true;
        }
    }
}
