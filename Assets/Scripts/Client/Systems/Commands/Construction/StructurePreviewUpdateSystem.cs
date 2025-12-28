using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Unity.Collections;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructurePreviewUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructurePreviewState>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;

            // 건설 모드가 아니거나, 선택된 프리팹이 없으면 무효
            if (userState.CurrentState != UserContext.Construction || previewState.SelectedPrefab == Entity.Null)
            {
                previewState.IsValidPlacement = false;
                return;
            }

            // 프리팹 유효성 및 크기 컴포넌트 확인
            Entity prefab = previewState.SelectedPrefab;
            if (!state.EntityManager.Exists(prefab) || !state.EntityManager.HasComponent<StructureFootprint>(prefab))
            {
                previewState.IsValidPlacement = false;
                return;
            }

            // [변경] Metadata 대신 Footprint 사용
            var footprint = state.EntityManager.GetComponentData<StructureFootprint>(prefab);
            int width = footprint.Width;
            int length = footprint.Length;

            // 그리드 설정
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();

            // 점유 확인
            bool isOccupied = false;
            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
                // previewState.GridPosition.x, y (int2) 사용
                isOccupied = GridUtility.IsOccupied(buffer, previewState.GridPosition.x, previewState.GridPosition.y,
                    width, length, gridSettings.GridSize.x, gridSettings.GridSize.y);
            }

            // 유닛 충돌 확인 (Physics OverlapAabb)
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float3 buildingCenter = GridUtility.GridToWorld(previewState.GridPosition.x, previewState.GridPosition.y,
                width, length, gridSettings);
            float3 halfExtents = new float3(
                width * gridSettings.CellSize * 0.5f,
                1f, // Y축 높이
                length * gridSettings.CellSize * 0.5f
            );
            bool hasUnitCollision = CheckUnitCollisionPhysics(physicsWorld, buildingCenter, halfExtents);

            previewState.IsValidPlacement = !isOccupied && !hasUnitCollision;
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