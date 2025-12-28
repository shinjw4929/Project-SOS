using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Burst; // Burst 추가
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile] // 시스템 전체 Burst 컴파일
    public partial struct HandleBuildRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 필수 싱글톤 검증 (HasSingleton 대신 TryGetSingletonEntity 사용이 더 빠름)
            if (!SystemAPI.TryGetSingletonEntity<GridSettings>(out var gridEntity) || 
                !SystemAPI.TryGetSingletonEntity<StructureCatalog>(out var catalogEntity))
            {
                return;
            }

            // 데이터 준비
            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            var prefabBuffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity);
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            
            // 그리드 버퍼가 없을 경우 대비
            DynamicBuffer<GridCell> gridBuffer = default;
            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // [최적화] foreach 대신 쿼리 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<BuildRequestRpc>>()
                         .WithEntityAccess())
            {
                // 함수 호출 인자 최적화
                ProcessBuildRequest(
                    ref state, 
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

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        [BurstCompile] // 내부 로직도 Burst로 최적화
        private void ProcessBuildRequest(
            ref SystemState state,
            EntityCommandBuffer ecb,
            ReceiveRpcCommandRequest rpcReceive,
            BuildRequestRpc rpc,
            Entity rpcEntity,
            GridSettings gridSettings,
            DynamicBuffer<GridCell> gridBuffer,
            DynamicBuffer<StructureCatalogElement> prefabBuffer,
            PhysicsWorldSingleton physicsWorld)
        {
            // 1. 유효성 검사 (빠른 실패)
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

            // 2. 컴포넌트 조회 (HasComponent 대신 TryGetComponent 권장, 여기선 로직상 Has 체크)
            if (!state.EntityManager.HasComponent<StructureFootprint>(structurePrefab))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            var footprint = state.EntityManager.GetComponentData<StructureFootprint>(structurePrefab);
            int width = footprint.Width;
            int length = footprint.Length;

            // 3. [최적화] Grid 검사 (Buffer 접근 최소화)
            if (gridBuffer.IsCreated)
            {
                // GridUtility가 Burst 호환이어야 함
                if (GridUtility.IsOccupied(gridBuffer, rpc.GridPosition.x, rpc.GridPosition.y, width, length,
                    gridSettings.GridSize.x, gridSettings.GridSize.y))
                {
                    ecb.DestroyEntity(rpcEntity);
                    return;
                }
            }

            // 4. [최적화] 물리 검사 (PhysicsWorld 접근)
            float3 buildingCenter = GridUtility.GridToWorld(rpc.GridPosition.x, rpc.GridPosition.y, width, length, gridSettings);
            float3 halfExtents = new float3(width * gridSettings.CellSize * 0.5f, 1f, length * gridSettings.CellSize * 0.5f);
            
            if (CheckUnitCollisionPhysics(physicsWorld, buildingCenter, halfExtents))
            {
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 5. 생성 로직
            CreateBuildingEntity(ref state, ecb, structurePrefab, rpc, width, length, gridSettings, rpcReceive.SourceConnection);

            ecb.DestroyEntity(rpcEntity);
        }

        private void CreateBuildingEntity(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity prefab,
            BuildRequestRpc rpc,
            int width,
            int length,
            GridSettings gridSettings,
            Entity connectionEntity)
        {
            Entity newStructure = ecb.Instantiate(prefab);

            float3 worldPos = GridUtility.GridToWorld(rpc.GridPosition.x, rpc.GridPosition.y, width, length, gridSettings);
            
            // 높이 보정
            if (state.EntityManager.HasComponent<StructureFootprint>(prefab))
            {
                worldPos.y += state.EntityManager.GetComponentData<StructureFootprint>(prefab).Height * 0.5f; 
            }

            // Transform 설정
            if (state.EntityManager.HasComponent<LocalTransform>(prefab))
            {
                var transform = state.EntityManager.GetComponentData<LocalTransform>(prefab);
                transform.Position = worldPos;
                ecb.SetComponent(newStructure, transform);
            }
            else
            {
                ecb.SetComponent(newStructure, LocalTransform.FromPosition(worldPos));
            }

            ecb.SetComponent(newStructure, new GridPosition { Position = rpc.GridPosition });

            // 소유자 설정 (NetworkId 컴포넌트 조회 최적화)
            if (state.EntityManager.HasComponent<NetworkId>(connectionEntity))
            {
                int ownerId = state.EntityManager.GetComponentData<NetworkId>(connectionEntity).Value;
                ecb.AddComponent(newStructure, new GhostOwner { NetworkId = ownerId });
                ecb.SetComponent(newStructure, new Team { teamId = ownerId });
            }

            if (state.EntityManager.HasComponent<ProductionInfo>(prefab))
            {
                var info = state.EntityManager.GetComponentData<ProductionInfo>(prefab);
                ecb.AddComponent(newStructure, new UnderConstructionTag
                {
                    Progress = 0f,
                    TotalBuildTime = info.ProductionTime
                });
            }
        }
        
        [BurstCompile]
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
                    BelongsTo = 1u << 6,
                    CollidesWith = (1u << 7) | (1u << 8),
                    GroupIndex = 0
                }
            };
            
            // NativeList 사용 시 Allocator.Temp는 Burst 내에서 매우 빠름
            var hits = new NativeList<int>(Allocator.Temp);
            bool hasCollision = physicsWorld.OverlapAabb(input, ref hits);
            hits.Dispose();

            return hasCollision;
        }
    }
}