using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// 건물 배치 프리뷰의 유효성 검사
    /// - GridCell 버퍼를 조회하여 점유 상태 확인
    /// - 유닛 충돌 검사
    /// - BuildingPreviewState.isValidPlacement 업데이트
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(GridOccupancySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct BuildingPreviewUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<BuildingPreviewState>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<BuildingEntitiesReferences>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            ref var previewState = ref SystemAPI.GetSingletonRW<BuildingPreviewState>().ValueRW;

            if (userState.CurrentState != UserContext.Construction)
            {
                previewState.isValidPlacement = false;
                return;
            }

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var buildingRefs = SystemAPI.GetSingleton<BuildingEntitiesReferences>();

            // 프리팹에서 건물 크기 조회
            Entity prefab = GetBuildingPrefab(previewState.selectedType, buildingRefs);
            if (prefab == Entity.Null || !state.EntityManager.HasComponent<BuildingMetadata>(prefab))
            {
                previewState.isValidPlacement = false;
                return;
            }

            var metadata = state.EntityManager.GetComponentData<BuildingMetadata>(prefab);
            int width = metadata.width;
            int height = metadata.height;

            // GridCell 버퍼로 점유 상태 확인
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            if (!SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                previewState.isValidPlacement = false;
                return;
            }

            var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            bool isOccupied = GridUtility.IsOccupied(buffer, previewState.gridX, previewState.gridY,
                width, height, gridSettings.gridWidth, gridSettings.gridHeight);

            bool hasUnitCollision = CheckUnitCollision(ref state, previewState.gridX, previewState.gridY,
                width, height, gridSettings);

            previewState.isValidPlacement = !isOccupied && !hasUnitCollision;
        }

        private Entity GetBuildingPrefab(BuildingTypeEnum type, BuildingEntitiesReferences refs)
        {
            return type switch
            {
                BuildingTypeEnum.Wall => refs.wallPrefabEntity,
                BuildingTypeEnum.Barracks => refs.barracksPrefabEntity,
                _ => Entity.Null
            };
        }

        private bool CheckUnitCollision(ref SystemState state, int gridX, int gridY, int width, int height, GridSettings gridSettings)
        {
            float3 buildingCenter = GridUtility.GridToWorld(gridX, gridY, width, height, gridSettings);
            float halfWidth = width * gridSettings.cellSize / 2f;
            float halfHeight = height * gridSettings.cellSize / 2f;

            foreach (var (transform, _) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitType>>())
            {
                float3 unitPos = transform.ValueRO.Position;

                if (unitPos.x >= buildingCenter.x - halfWidth && unitPos.x <= buildingCenter.x + halfWidth &&
                    unitPos.z >= buildingCenter.z - halfHeight && unitPos.z <= buildingCenter.z + halfHeight)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
