using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Shared;

namespace Client
{
    [BurstCompile]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructurePreviewUpdateSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructurePreviewState>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            
            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;

            // 1. 기본 조건 체크
            if (userState.CurrentState != UserContext.Construction || previewState.SelectedPrefab == Entity.Null)
            {
                previewState.IsValidPlacement = false;
                return;
            }

            // 2. 프리팹 데이터 확인
            _footprintLookup.Update(ref state);
            if (!_footprintLookup.HasComponent(previewState.SelectedPrefab))
            {
                previewState.IsValidPlacement = false;
                return;
            }
            
            var footprint = _footprintLookup[previewState.SelectedPrefab];
            int width = footprint.Width;
            int length = footprint.Length;

            // 3. 그리드 점유 확인
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            bool isOccupied = false;

            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
                isOccupied = GridUtility.IsOccupied(buffer, previewState.GridPosition.x, previewState.GridPosition.y,
                    width, length, gridSettings.GridSize.x, gridSettings.GridSize.y);
            }

            // 4. [수정됨] 유닛 물리 충돌 확인 (최신 API 적용)
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float3 buildingCenter = GridUtility.GridToWorld(previewState.GridPosition.x, previewState.GridPosition.y,
                width, length, gridSettings);
            
            float3 halfExtents = new float3(
                width * gridSettings.CellSize * 0.5f,
                1f, 
                length * gridSettings.CellSize * 0.5f
            );

            bool hasUnitCollision = CheckCollision(ref physicsWorld, buildingCenter, halfExtents);

            previewState.IsValidPlacement = !isOccupied && !hasUnitCollision;
        }

        [BurstCompile]
        private bool CheckCollision(ref PhysicsWorldSingleton physicsWorld, float3 center, float3 halfExtents)
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

            // [변경 사항]
            // Unity Physics 1.x에서는 ICollector 대신 NativeList<int>를 직접 받습니다.
            // Allocator.Temp는 매우 빠르므로 프레임 드랍 걱정은 안 하셔도 됩니다.
            var hits = new NativeList<int>(Allocator.Temp);
            
            // OverlapAabb는 bool을 반환하여 히트 여부를 알려줍니다. (버전에 따라 반환값이 없을 수도 있어 Length 체크가 가장 안전합니다)
            physicsWorld.PhysicsWorld.CollisionWorld.OverlapAabb(input, ref hits);
            
            bool hasHit = hits.Length > 0;
            
            hits.Dispose(); // Temp 할당 해제
            
            return hasHit;
        }
    }
}