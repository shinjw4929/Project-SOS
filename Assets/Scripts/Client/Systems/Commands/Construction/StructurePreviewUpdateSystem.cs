using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Shared;

namespace Client
{
    [BurstCompile]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructurePreviewUpdateSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
        [ReadOnly] private BufferLookup<GridCell> _gridCellLookup;
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        [ReadOnly] private ComponentLookup<BuildRange> _buildRangeLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructurePreviewState>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<CurrentSelectionState>();

            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _gridCellLookup = state.GetBufferLookup<GridCell>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _buildRangeLookup = state.GetComponentLookup<BuildRange>(true);
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
                previewState.Status = PlacementStatus.Invalid;
                return;
            }

            // 2. 프리팹 데이터 확인
            _footprintLookup.Update(ref state);
            if (!_footprintLookup.TryGetComponent(previewState.SelectedPrefab, out var footprint))
            {
                previewState.IsValidPlacement = false;
                previewState.Status = PlacementStatus.Invalid;
                return;
            }

            int width = footprint.Width;
            int length = footprint.Length;

            // 3. 그리드 점유 확인
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            bool isOccupied = false;

            _gridCellLookup.Update(ref state);
            if (_gridCellLookup.TryGetBuffer(gridEntity, out var buffer))
            {
                isOccupied = GridUtility.IsOccupied(buffer, previewState.GridPosition.x, previewState.GridPosition.y,
                    width, length, gridSettings.GridSize.x, gridSettings.GridSize.y);
            }

            // 4. 유닛 물리 충돌 확인
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float3 buildingCenter = GridUtility.GridToWorld(previewState.GridPosition.x, previewState.GridPosition.y,
                width, length, gridSettings);

            float3 halfExtents = new float3(
                width * gridSettings.CellSize * 0.5f,
                1f,
                length * gridSettings.CellSize * 0.5f
            );

            bool hasUnitCollision = CheckCollision(ref physicsWorld, buildingCenter, halfExtents);

            // 5. 기본 유효성 판단 (그리드 점유 + 유닛 충돌)
            bool isValidPlacement = !isOccupied && !hasUnitCollision;
            previewState.IsValidPlacement = isValidPlacement;

            // 6. 사거리 계산 (유효한 배치일 때만)
            if (!isValidPlacement)
            {
                previewState.Status = PlacementStatus.Invalid;
                previewState.DistanceToBuilder = float.MaxValue;
                return;
            }

            // 7. PrimaryEntity의 위치와 BuildRange 조회
            var selectionState = SystemAPI.GetSingleton<CurrentSelectionState>();
            Entity builderEntity = selectionState.PrimaryEntity;

            _transformLookup.Update(ref state);
            _buildRangeLookup.Update(ref state);

            // Builder 엔티티가 유효하고 위치 정보가 있는지 확인
            if (builderEntity == Entity.Null || !_transformLookup.HasComponent(builderEntity))
            {
                // Builder 정보 없으면 사거리 내로 간주 (기본 동작)
                previewState.Status = PlacementStatus.ValidInRange;
                previewState.DistanceToBuilder = 0f;
                return;
            }

            float3 builderPos = _transformLookup[builderEntity].Position;

            // BuildRange 컴포넌트가 있는지 확인
            float buildRange = float.MaxValue; // 기본값: 무한대 (제한 없음)
            if (_buildRangeLookup.HasComponent(builderEntity))
            {
                buildRange = _buildRangeLookup[builderEntity].Value;
            }

            // 8. AABB 최근접점까지의 거리 계산
            float3 aabbMin = buildingCenter - halfExtents;
            float3 aabbMax = buildingCenter + halfExtents;
            float distance = CalculateDistanceToAABB(builderPos, aabbMin, aabbMax);

            previewState.DistanceToBuilder = distance;

            // 9. 사거리 내/외 판단
            if (distance <= buildRange)
            {
                previewState.Status = PlacementStatus.ValidInRange;
            }
            else
            {
                previewState.Status = PlacementStatus.ValidOutOfRange;
            }
        }

        /// <summary>
        /// 점(point)에서 AABB까지의 XZ 평면 거리를 계산합니다.
        /// Y축은 무시하고 수평 거리만 계산합니다.
        /// </summary>
        [BurstCompile]
        private static float CalculateDistanceToAABB(float3 point, float3 aabbMin, float3 aabbMax)
        {
            // XZ 평면에서 AABB 최근접점 계산
            float closestX = math.clamp(point.x, aabbMin.x, aabbMax.x);
            float closestZ = math.clamp(point.z, aabbMin.z, aabbMax.z);

            // 수평 거리 계산
            float dx = point.x - closestX;
            float dz = point.z - closestZ;

            return math.sqrt(dx * dx + dz * dz);
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

            var hits = new NativeList<int>(Allocator.Temp);
            physicsWorld.PhysicsWorld.CollisionWorld.OverlapAabb(input, ref hits);

            bool hasHit = hits.Length > 0;
            hits.Dispose();

            return hasHit;
        }
    }
}
