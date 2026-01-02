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
        // 1. 컴포넌트 데이터에 빠르게 접근하기 위한 Lookup 필드 선언
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<ProductionInfo> _productionInfoLookup;
        [ReadOnly] private ComponentLookup<NeedsNavMeshObstacle> _needsNavMeshLookup; // Enableable 컴포넌트 체크용

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            // Lookup 초기화 (ReadOnly 설정으로 안전성 및 성능 확보)
            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _productionInfoLookup = state.GetComponentLookup<ProductionInfo>(true);
            _needsNavMeshLookup = state.GetComponentLookup<NeedsNavMeshObstacle>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 필수 싱글톤 확인
            if (!SystemAPI.TryGetSingletonEntity<GridSettings>(out var gridEntity) || 
                !SystemAPI.TryGetSingletonEntity<StructureCatalog>(out var catalogEntity))
            {
                return;
            }

            // 2. Lookup 갱신 (프레임마다 필수)
            _footprintLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _productionInfoLookup.Update(ref state);
            _needsNavMeshLookup.Update(ref state);

            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            var prefabBuffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity);
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            
            DynamicBuffer<GridCell> gridBuffer = default;
            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            }

            // 3. 시스템 ECB 사용 (직접 생성보다 메모리 관리 및 병합 시점이 명확함)
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // 4. 쿼리 순회
            foreach (var (rpcReceive, rpc, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<BuildRequestRpc>>()
                         .WithEntityAccess())
            {
                ProcessBuildRequest(
                    ecb, 
                    rpcReceive.ValueRO, 
                    rpc.ValueRO, 
                    rpcEntity, 
                    gridSettings, 
                    gridBuffer, 
                    prefabBuffer, 
                    physicsWorld
                );
            }
        }

        // ref SystemState 제거 -> EntityManager 접근을 차단하고 Lookup 사용
        private void ProcessBuildRequest(
            EntityCommandBuffer ecb,
            ReceiveRpcCommandRequest rpcReceive,
            BuildRequestRpc rpc,
            Entity rpcEntity,
            GridSettings gridSettings,
            DynamicBuffer<GridCell> gridBuffer,
            DynamicBuffer<StructureCatalogElement> prefabBuffer,
            PhysicsWorldSingleton physicsWorld)
        {
            // 유효성 검사
            int index = rpc.StructureIndex;
            if (index < 0 || index >= prefabBuffer.Length)
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            Entity structurePrefab = prefabBuffer[index].PrefabEntity;
            if (structurePrefab == Entity.Null)
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // Lookup을 통한 데이터 조회 (EntityManager 접근보다 훨씬 빠름)
            if (!_footprintLookup.HasComponent(structurePrefab))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            var footprint = _footprintLookup[structurePrefab];
            int width = footprint.Width;
            int length = footprint.Length;

            // Grid 검사
            if (gridBuffer.IsCreated)
            {
                if (GridUtility.IsOccupied(gridBuffer, rpc.GridPosition.x, rpc.GridPosition.y, width, length,
                    gridSettings.GridSize.x, gridSettings.GridSize.y))
                {
                    ecb.DestroyEntity(rpcEntity);
                    return;
                }
            }

            // 물리 검사
            float3 buildingCenter = GridUtility.GridToWorld(rpc.GridPosition.x, rpc.GridPosition.y, width, length, gridSettings);
            float3 halfExtents = new float3(width * gridSettings.CellSize * 0.5f, 1f, length * gridSettings.CellSize * 0.5f);
            
            if (CheckUnitCollisionPhysics(physicsWorld, buildingCenter, halfExtents))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 생성 로직 호출
            CreateBuildingEntity(ecb, structurePrefab, rpc, width, length, gridSettings, buildingCenter, rpcReceive.SourceConnection);

            ecb.DestroyEntity(rpcEntity);
        }

        private void CreateBuildingEntity(
            EntityCommandBuffer ecb,
            Entity prefab,
            BuildRequestRpc rpc,
            int width,
            int length,
            GridSettings gridSettings,
            float3 worldPos, // 계산된 위치를 인자로 받음
            Entity connectionEntity)
        {
            Entity newStructure = ecb.Instantiate(prefab);

            // 높이 보정 (Lookup 사용)
            if (_footprintLookup.HasComponent(prefab))
            {
                worldPos.y += _footprintLookup[prefab].Height * 0.5f; 
            }

            // Transform 설정 (Lookup 사용)
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

            // 소유자 설정 (Lookup 사용)
            if (_networkIdLookup.HasComponent(connectionEntity))
            {
                int ownerId = _networkIdLookup[connectionEntity].Value;
                ecb.AddComponent(newStructure, new GhostOwner { NetworkId = ownerId });
                ecb.SetComponent(newStructure, new Team { teamId = ownerId });
            }

            // 생산 정보 설정 (Lookup 사용)
            if (_productionInfoLookup.HasComponent(prefab))
            {
                var info = _productionInfoLookup[prefab];
                ecb.AddComponent(newStructure, new UnderConstructionTag
                {
                    Progress = 0f,
                    TotalBuildTime = info.ProductionTime
                });
            }

            // NavMeshObstacle 태그 (Enableable Component Lookup 사용)
            if (_needsNavMeshLookup.HasComponent(prefab))
            {
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(newStructure, true);
            }
        }
        
        // 정적 메서드나 Burst로 컴파일된 멤버 메서드 사용 권장
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
                    BelongsTo = 1u << 6,            // Structure Layer
                    CollidesWith = (1u << 7) | (1u << 8), // Unit | Obstacle
                    GroupIndex = 0
                }
            };
            
            // NativeList를 사용하여 Overlap 결과를 받지만, 내용물은 필요 없으므로 바로 Dispose
            var hits = new NativeList<int>(Allocator.Temp);
            bool hasCollision = physicsWorld.OverlapAabb(input, ref hits);
            hits.Dispose();

            return hasCollision;
        }
    }
}