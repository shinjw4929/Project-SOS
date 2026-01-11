using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
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
        [ReadOnly] private ComponentLookup<WorkRange> _workRangeLookup;
        [ReadOnly] private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;
        [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;

        private EntityQuery _userCurrencyQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructurePreviewState>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<SelectedEntityInfoState>();

            _footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
            _gridCellLookup = state.GetBufferLookup<GridCell>(true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _workRangeLookup = state.GetComponentLookup<WorkRange>(true);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
            _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);

            _userCurrencyQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<UserCurrency>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>()
            );
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
            var selectedEntityInfoState = SystemAPI.GetSingleton<SelectedEntityInfoState>();
            float3 buildingCenter = GridUtility.GridToWorld(previewState.GridPosition.x, previewState.GridPosition.y,
                width, length, gridSettings);

            float3 halfExtents = new float3(
                width * gridSettings.CellSize * 0.5f,
                1f,
                length * gridSettings.CellSize * 0.5f
            );

            Entity builderEntity = selectedEntityInfoState.PrimaryEntity;

            if (builderEntity == Entity.Null) return;
            
            bool hasUnitCollision = CheckCollision(ref physicsWorld, buildingCenter, halfExtents, builderEntity);

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

            _transformLookup.Update(ref state);
            _workRangeLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);

            // Builder 엔티티가 유효하고 위치 정보가 있는지 확인
            if (!_transformLookup.HasComponent(builderEntity))
            {
                // Builder 정보 없으면 사거리 내로 간주 (기본 동작)
                previewState.Status = PlacementStatus.ValidInRange;
                previewState.DistanceToBuilder = 0f;
                return;
            }

            float3 builderPos = _transformLookup[builderEntity].Position;

            // BuildRange 컴포넌트가 있는지 확인
            float buildRange = float.MaxValue; // 기본값: 무한대 (제한 없음)
            if (_workRangeLookup.HasComponent(builderEntity))
            {
                buildRange = _workRangeLookup[builderEntity].Value;
            }

            // 건물 반지름 조회
            float structureRadius = 1.5f; // 기본값
            if (_obstacleRadiusLookup.HasComponent(previewState.SelectedPrefab))
            {
                structureRadius = _obstacleRadiusLookup[previewState.SelectedPrefab].Radius;
            }

            // 8. 중심점 거리 계산 (XZ 평면) - 건물 반지름 빼기
            float centerDistance = math.distance(
                new float2(builderPos.x, builderPos.z),
                new float2(buildingCenter.x, buildingCenter.z)
            );
            float distanceToSurface = centerDistance - structureRadius;

            previewState.DistanceToBuilder = distanceToSurface;

            // 9. 비용 확인
            _productionCostLookup.Update(ref state);
            bool hasEnoughCurrency = true;
            if (_productionCostLookup.TryGetComponent(previewState.SelectedPrefab, out var cost))
            {
                // 멀티플레이어 환경에서 각 유저마다 UserCurrency가 존재하므로
                // GhostOwnerIsLocal 필터로 현재 클라이언트 소유만 조회
                if (_userCurrencyQuery.CalculateEntityCount() == 1)
                {
                    var userCurrency = _userCurrencyQuery.GetSingleton<UserCurrency>();
                    hasEnoughCurrency = userCurrency.Amount >= cost.Cost;
                }
            }

            // 10. 최종 상태 판단: 돈 부족 시 Invalid
            if (!hasEnoughCurrency)
            {
                previewState.Status = PlacementStatus.Invalid;
                return;
            }

            // 11. 사거리 내/외 판단
            // WorkRange는 이미 유닛 반지름이 포함되어 있음 (workRange + radius)
            if (distanceToSurface <= buildRange)
            {
                previewState.Status = PlacementStatus.ValidInRange;
            }
            else
            {
                previewState.Status = PlacementStatus.ValidOutOfRange;
            }
        }

        [BurstCompile]
        private bool CheckCollision(ref PhysicsWorldSingleton physicsWorld, float3 center, float3 halfExtents, Entity builderEntity)
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
                    BelongsTo = 1u << 7, // Structure Layer
                    CollidesWith = (1u << 11) | (1u << 12), // Unit | Enemy
                    GroupIndex = 0
                }
            };

            var hits = new NativeList<int>(Allocator.Temp);
            physicsWorld.PhysicsWorld.CollisionWorld.OverlapAabb(input, ref hits);

            // 건설자(builderEntity)를 제외한 충돌 검사
            bool hasBlockingCollision = false;
            for (int i = 0; i < hits.Length; i++)
            {
                int bodyIndex = hits[i];
                Entity hitEntity = physicsWorld.PhysicsWorld.Bodies[bodyIndex].Entity;

                // 건설자 제외
                if (builderEntity != Entity.Null && hitEntity == builderEntity)
                {
                    continue; // 건설자는 충돌에서 제외
                }

                // 건설자가 아닌 다른 유닛/적과 충돌
                hasBlockingCollision = true;
                break;
            }

            hits.Dispose();
            return hasBlockingCollision;
        }
    }
}
