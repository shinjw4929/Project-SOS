using Unity.Entities;
using Unity.Transforms;

namespace Shared
{
    /// <summary>
    /// 건물 위치를 기반으로 그리드 점유 상태를 매 프레임 갱신
    /// 클라이언트/서버 모두에서 실행
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GridOccupancySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();

            if (!SystemAPI.HasBuffer<GridCell>(gridEntity))
                return;

            var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);

            // 1. 모든 셀 초기화 (false)
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = new GridCell { isOccupied = false };
            }

            // 2. 모든 건물의 점유 영역 마킹
            foreach (var (building, transform) in
                SystemAPI.Query<RefRO<Building>, RefRO<LocalTransform>>()
                    .WithNone<Prefab>())
            {
                // Building.buildingType으로 프리팹에서 메타데이터 조회
                int width, height;
                if (!TryGetBuildingSize(ref state, building.ValueRO.buildingType, out width, out height))
                    continue;

                var gridPos = GridUtility.WorldToGridForBuilding(
                    transform.ValueRO.Position,
                    width,
                    height,
                    gridSettings);

                GridUtility.MarkOccupied(buffer, gridPos.x, gridPos.y, width, height, gridSettings.gridWidth);
            }
        }

        private bool TryGetBuildingSize(ref SystemState state, BuildingTypeEnum type, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!SystemAPI.HasSingleton<BuildingEntitiesReferences>())
                return false;

            var refs = SystemAPI.GetSingleton<BuildingEntitiesReferences>();
            Entity prefab = type switch
            {
                BuildingTypeEnum.Wall => refs.wallPrefabEntity,
                BuildingTypeEnum.Barracks => refs.barracksPrefabEntity,
                _ => Entity.Null
            };

            if (prefab == Entity.Null || !state.EntityManager.HasComponent<BuildingMetadata>(prefab))
                return false;

            var metadata = state.EntityManager.GetComponentData<BuildingMetadata>(prefab);
            width = metadata.width;
            height = metadata.height;
            return true;
        }
    }
}
