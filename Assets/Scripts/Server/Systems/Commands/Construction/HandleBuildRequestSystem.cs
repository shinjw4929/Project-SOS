using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Burst;
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleBuildRequestSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<ProductionInfo> _productionInfoLookup;
        [ReadOnly] private ComponentLookup<NeedsNavMeshObstacle> _needsNavMeshLookup;
        
        // 자원을 수정해야 하므로 ReadOnly 아님
        private ComponentLookup<UserCurrency> _userCurrencyLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);
            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _productionInfoLookup = state.GetComponentLookup<ProductionInfo>(true);
            _needsNavMeshLookup = state.GetComponentLookup<NeedsNavMeshObstacle>(true);
            _userCurrencyLookup = state.GetComponentLookup<UserCurrency>(false); 
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<GridSettings>(out var gridEntity) || 
                !SystemAPI.TryGetSingletonEntity<StructureCatalog>(out var catalogEntity))
            {
                return;
            }

            _productionCostLookup.Update(ref state);
            _footprintLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _productionInfoLookup.Update(ref state);
            _needsNavMeshLookup.Update(ref state);
            _userCurrencyLookup.Update(ref state);

            // NetworkId -> UserCurrency Entity 매핑
            var networkIdToCurrencyEntity = new NativeHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                         .WithAll<UserEconomyTag>()
                         .WithEntityAccess())
            {
                networkIdToCurrencyEntity.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
            }

            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            var prefabBuffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity);
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            
            DynamicBuffer<GridCell> gridBuffer = default;
            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            }

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (rpcReceive, rpc, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<BuildRequestRpc>>()
                         .WithEntityAccess())
            {
                if (!_networkIdLookup.HasComponent(rpcReceive.ValueRO.SourceConnection))
                {
                    ecb.DestroyEntity(rpcEntity);
                    continue;
                }
                
                int sourceNetworkId = _networkIdLookup[rpcReceive.ValueRO.SourceConnection].Value;

                ProcessBuildRequest(
                    ecb, 
                    sourceNetworkId,
                    rpc.ValueRO, 
                    rpcEntity, 
                    gridSettings, 
                    gridBuffer, 
                    prefabBuffer, 
                    physicsWorld,
                    networkIdToCurrencyEntity
                );
            }

            networkIdToCurrencyEntity.Dispose();
        }

        private void ProcessBuildRequest(
            EntityCommandBuffer ecb,
            int sourceNetworkId,
            BuildRequestRpc rpc,
            Entity rpcEntity,
            GridSettings gridSettings,
            DynamicBuffer<GridCell> gridBuffer,
            DynamicBuffer<StructureCatalogElement> prefabBuffer,
            PhysicsWorldSingleton physicsWorld,
            NativeHashMap<int, Entity> networkIdToCurrencyMap)
        {
            // 1. 프리팹 유효성 검사
            int index = rpc.StructureIndex;
            if (index < 0 || index >= prefabBuffer.Length)
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            Entity structurePrefab = prefabBuffer[index].PrefabEntity;
            if (structurePrefab == Entity.Null || 
                !_productionCostLookup.HasComponent(structurePrefab) || 
                !_footprintLookup.HasComponent(structurePrefab))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 2. 자원 보유량 '확인' (차감은 아직 안 함)
            int constructionCost = _productionCostLookup[structurePrefab].Cost;
            Entity userCurrencyEntity = Entity.Null;

            if (networkIdToCurrencyMap.TryGetValue(sourceNetworkId, out userCurrencyEntity))
            {
                // 현재 자원량 확인
                int currencyAmount = _userCurrencyLookup[userCurrencyEntity].Amount;
                if (currencyAmount < constructionCost)
                {
                    // 자원 부족 -> 실패
                    ecb.DestroyEntity(rpcEntity);
                    return;
                }
            }
            else
            {
                // 자원 엔티티 없음 -> 실패
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 3. Grid 및 물리 검사 (건설 위치 유효성 검증)
            var footprint = _footprintLookup[structurePrefab];
            int width = footprint.Width;
            int length = footprint.Length;

            // 3-1. Grid 점유 확인
            if (gridBuffer.IsCreated)
            {
                if (GridUtility.IsOccupied(gridBuffer, rpc.GridPosition.x, rpc.GridPosition.y, width, length,
                    gridSettings.GridSize.x, gridSettings.GridSize.y))
                {
                    // 이미 건물이 있음 -> 실패
                    ecb.DestroyEntity(rpcEntity);
                    return;
                }
            }

            // 3-2. 물리 충돌 확인 (유닛 등)
            float3 buildingCenter = GridUtility.GridToWorld(rpc.GridPosition.x, rpc.GridPosition.y, width, length, gridSettings);
            float3 halfExtents = new float3(width * gridSettings.CellSize * 0.5f, 1f, length * gridSettings.CellSize * 0.5f);
            
            if (CheckUnitCollisionPhysics(physicsWorld, buildingCenter, halfExtents))
            {
                // 유닛이 비키지 않음 -> 실패
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 4. [최종 승인] 자원 차감 (Commit)
            // 위 모든 검사를 통과했으므로 이제 안전하게 돈을 뺍니다.
            RefRW<UserCurrency> userCurrencyRW = _userCurrencyLookup.GetRefRW(userCurrencyEntity);
            userCurrencyRW.ValueRW.Amount -= constructionCost;

            // 5. 엔티티 생성 실행
            CreateBuildingEntity(ecb, structurePrefab, rpc, width, length, gridSettings, buildingCenter, sourceNetworkId);

            ecb.DestroyEntity(rpcEntity);
        }

        private void CreateBuildingEntity(
            EntityCommandBuffer ecb,
            Entity prefab,
            BuildRequestRpc rpc,
            int width,
            int length,
            GridSettings gridSettings,
            float3 worldPos,
            int ownerNetworkId)
        {
            Entity newStructure = ecb.Instantiate(prefab);

            if (_footprintLookup.HasComponent(prefab))
            {
                worldPos.y += _footprintLookup[prefab].Height * 0.5f; 
            }

            if (_transformLookup.HasComponent(prefab))
            {
                var transform = _transformLookup[prefab];
                transform.Position = worldPos;
                ecb.SetComponent(newStructure, transform);
            }
            else
            {
                ecb.SetComponent(newStructure, LocalTransform.FromPosition(worldPos));
            }

            ecb.SetComponent(newStructure, new GridPosition { Position = rpc.GridPosition });

            ecb.AddComponent(newStructure, new GhostOwner { NetworkId = ownerNetworkId });
            ecb.SetComponent(newStructure, new Team { teamId = ownerNetworkId });

            if (_productionInfoLookup.HasComponent(prefab))
            {
                var info = _productionInfoLookup[prefab];
                ecb.AddComponent(newStructure, new UnderConstructionTag
                {
                    Progress = 0f,
                    TotalBuildTime = info.ProductionTime
                });
            }

            if (_needsNavMeshLookup.HasComponent(prefab))
            {
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(newStructure, true);
            }
        }
        
        private bool CheckUnitCollisionPhysics(PhysicsWorldSingleton physicsWorld, float3 center, float3 halfExtents)
        {
            var input = new OverlapAabbInput
            {
                Aabb = new Aabb
                {
                    Min = center - halfExtents,
                    Max = center + halfExtents
                },
                Filter = new CollisionFilter
                {
                    BelongsTo = 1u << 7,            
                    CollidesWith = (1u << 11) | (1u << 12),
                    GroupIndex = 0
                }
            };
            
            var hits = new NativeList<int>(Allocator.Temp);
            bool hasCollision = physicsWorld.OverlapAabb(input, ref hits);
            hits.Dispose();

            return hasCollision;
        }
    }
}