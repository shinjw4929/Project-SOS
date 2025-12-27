using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Shared; // StructureFootprint, GridSettings, StructureTag, GridPosition 등

namespace Server
{
    /// <summary>
    /// 클라이언트의 건물 건설 요청 RPC를 처리하는 서버 시스템
    /// - Index 기반 프리팹 조회로 네트워크 동기화 문제 해결
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct HandleBuildRequestSystem : ISystem
    {
        private bool _singletonWarningLogged;

        public void OnCreate(ref SystemState state)
        {
            _singletonWarningLogged = false;
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<StructureEntitiesReferences>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 필수 싱글톤 검증
            if (!SystemAPI.HasSingleton<GridSettings>() || !SystemAPI.HasSingleton<StructureEntitiesReferences>())
            {
                if (!_singletonWarningLogged)
                {
                    UnityEngine.Debug.LogWarning("[HandleBuildRequestSystem] GridSettings or StructureEntitiesReferences singleton not found.");
                    _singletonWarningLogged = true;
                }
                ConsumeAndDestroyRpcs(ref state);
                return;
            }

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            
            // [추가] 프리팹 버퍼 가져오기
            var refsEntity = SystemAPI.GetSingletonEntity<StructureEntitiesReferences>();
            var prefabBuffer = SystemAPI.GetBuffer<StructurePrefabElement>(refsEntity);
            
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 모든 건설 요청 RPC 처리
            foreach (var (rpcReceive, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                         .WithAll<BuildRequestRpc>()
                         .WithEntityAccess())
            {
                // [변경] buffer를 인자로 전달
                ProcessBuildRequest(ref state, ref ecb, rpcReceive, rpcEntity, gridSettings, prefabBuffer);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private void ConsumeAndDestroyRpcs(ref SystemState state)
        {
            var tempEcb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                .WithAll<BuildRequestRpc>()
                .WithEntityAccess())
            {
                tempEcb.DestroyEntity(rpcEntity);
            }
            tempEcb.Playback(state.EntityManager);
            tempEcb.Dispose();
        }

        /// <summary>
        /// 개별 건설 요청 처리
        /// </summary>
        private void ProcessBuildRequest(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            RefRO<ReceiveRpcCommandRequest> rpcReceive,
            Entity rpcEntity,
            GridSettings gridSettings,
            DynamicBuffer<StructurePrefabElement> prefabBuffer) // [인자 추가]
        {
            var rpc = state.EntityManager.GetComponentData<BuildRequestRpc>(rpcEntity);
            
            // 1. [변경] RPC로 받은 Index를 사용하여 서버 월드의 프리팹 엔티티 찾기
            // (Entity를 직접 보내면 클라/서버 간 ID 불일치로 실패함)
            int index = rpc.StructureIndex;

            if (index < 0 || index >= prefabBuffer.Length)
            {
                UnityEngine.Debug.LogWarning($"[Server] Invalid Structure Index received: {index}");
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            Entity structurePrefab = prefabBuffer[index].PrefabEntity;
            
            // 2. [검증] 엔티티 유효성 확인
            if (structurePrefab == Entity.Null || !state.EntityManager.Exists(structurePrefab))
            {
                UnityEngine.Debug.LogWarning($"[Server] Structure Prefab is Null/Invalid at index {index}.");
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 3. [검증] 필수 컴포넌트(Footprint) 확인
            if (!state.EntityManager.HasComponent<StructureFootprint>(structurePrefab))
            {
                UnityEngine.Debug.LogWarning($"[Server] Prefab does not have StructureFootprint component.");
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 4. 데이터 추출
            var footprint = state.EntityManager.GetComponentData<StructureFootprint>(structurePrefab);
            int width = footprint.Width;
            int length = footprint.Length;

            // 5. [검증] 그리드 충돌 (GridCell 버퍼 확인)
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
                
                if (GridUtility.IsOccupied(buffer, rpc.GridPosition.x, rpc.GridPosition.y, width, length,
                    gridSettings.GridSize.x, gridSettings.GridSize.y))
                {
                    UnityEngine.Debug.Log("[Server] Build failed: Grid Occupied");
                    ecb.DestroyEntity(rpcEntity);
                    return;
                }
            }

            // 6. [검증] 유닛 충돌 (Physics OverlapAabb)
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float3 buildingCenter = GridUtility.GridToWorld(rpc.GridPosition.x, rpc.GridPosition.y, width, length, gridSettings);
            float3 halfExtents = new float3(
                width * gridSettings.CellSize * 0.5f,
                1f,
                length * gridSettings.CellSize * 0.5f
            );
            if (CheckUnitCollisionPhysics(physicsWorld, buildingCenter, halfExtents))
            {
                UnityEngine.Debug.Log("[Server] Build failed: Unit Collision");
                ecb.DestroyEntity(rpcEntity);
                return;
            }

            // 7. [성공] 건물 생성
            var networkId = SystemAPI.GetComponent<NetworkId>(rpcReceive.ValueRO.SourceConnection);
            CreateBuildingEntity(ref state, ref ecb, structurePrefab, rpc, width, length, gridSettings, networkId.Value);

            // 8. RPC 요청 엔티티 삭제 (처리 완료)
            ecb.DestroyEntity(rpcEntity);
        }
        
        private void CreateBuildingEntity(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            Entity prefab,
            BuildRequestRpc rpc,
            int width,
            int length,
            GridSettings gridSettings,
            int ownerNetworkId)
        {
            Entity newStructure = ecb.Instantiate(prefab);

            // 1. 월드 위치 계산
            float3 worldPos = GridUtility.GridToWorld(rpc.GridPosition.x, rpc.GridPosition.y, width, length, gridSettings);
    
            // [중요] 높이 보정 (Visual Height)
            if (state.EntityManager.HasComponent<StructureFootprint>(prefab))
            {
                var footprint = state.EntityManager.GetComponentData<StructureFootprint>(prefab);
                worldPos.y += footprint.Height * 0.5f; 
            }

            // 2. [수정] 프리팹의 Transform을 복사해서 위치만 변경!
            // LocalTransform.FromPosition()을 쓰면 스케일이 1로 초기화되는 문제 해결
            var transform = state.EntityManager.GetComponentData<LocalTransform>(prefab);
            transform.Position = worldPos;
            ecb.SetComponent(newStructure, transform);

            // 3. GridPosition 컴포넌트 추가
            ecb.SetComponent(newStructure, new GridPosition 
            { 
                Position = rpc.GridPosition
            });

            // 4. 소유자 설정
            ecb.AddComponent(newStructure, new GhostOwner { NetworkId = ownerNetworkId });

            // 5. 팀 ID 설정 (소유자의 NetworkId를 팀 ID로 사용)
            ecb.SetComponent(newStructure, new Team { teamId = ownerNetworkId });

            // 6. 건설 태그 추가
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
        
        private bool CheckUnitCollisionPhysics(PhysicsWorldSingleton physicsWorld, float3 center, float3 halfExtents)
        {
            // Structure(6) → Unit(7) + Enemy(8) 충돌 체크
            var input = new OverlapAabbInput
            {
                Aabb = new Aabb
                {
                    Min = center - halfExtents,
                    Max = center + halfExtents
                },
                Filter = new CollisionFilter
                {
                    BelongsTo = 1u << 6,                      // Structure
                    CollidesWith = (1u << 7) | (1u << 8),    // Unit + Enemy
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