using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 그리드 좌표 변환 및 건물 배치 검증 유틸리티
    /// </summary>
    public static class GridUtility
    {
        /// <summary>
        /// 월드 좌표를 그리드 좌표로 변환
        /// </summary>
        public static int2 WorldToGrid(float3 worldPos, GridSettings settings)
        {
            return new int2(
                (int)math.floor((worldPos.x - settings.gridOrigin.x) / settings.cellSize),
                (int)math.floor((worldPos.z - settings.gridOrigin.y) / settings.cellSize)
            );
        }

        /// <summary>
        /// 그리드 좌표를 월드 좌표로 변환 (건물 중심점)
        /// </summary>
        public static float3 GridToWorld(int gridX, int gridY, int width, int height, GridSettings settings)
        {
            return new float3(
                settings.gridOrigin.x + gridX * settings.cellSize + (width * settings.cellSize) / 2f,
                0,
                settings.gridOrigin.y + gridY * settings.cellSize + (height * settings.cellSize) / 2f
            );
        }

        /// <summary>
        /// 두 사각형 영역이 겹치는지 검사 (AABB collision)
        /// </summary>
        public static bool IsOverlapping(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2)
        {
            return !(x1 + w1 <= x2 || x2 + w2 <= x1 || y1 + h1 <= y2 || y2 + h2 <= y1);
        }

        /// <summary>
        /// 건물 타입에 따른 그리드 크기 반환
        /// TODO: 향후 BuildingMetadata 컴포넌트를 사용하여 데이터 주도 방식으로 변경 가능
        /// (프리팹에 BuildingMetadata를 추가하고 런타임에 쿼리)
        /// </summary>
        public static void GetBuildingSize(BuildingTypeEnum type, out int width, out int height)
        {
            switch (type)
            {
                case BuildingTypeEnum.Wall:
                    width = 2;
                    height = 2;
                    break;
                case BuildingTypeEnum.Barracks:
                    width = 3;
                    height = 3;
                    break;
                default:
                    width = 1;
                    height = 1;
                    break;
            }
        }

        /// <summary>
        /// 건물 너비 반환
        /// </summary>
        public static int GetBuildingWidth(BuildingTypeEnum type)
        {
            GetBuildingSize(type, out int width, out _);
            return width;
        }

        /// <summary>
        /// 건물 높이 반환
        /// </summary>
        public static int GetBuildingHeight(BuildingTypeEnum type)
        {
            GetBuildingSize(type, out _, out int height);
            return height;
        }

        /// <summary>
        /// 건물 타입에 따른 Y축 오프셋 반환 (메시 중심을 지면 위에 배치하기 위함)
        /// </summary>
        public static float GetBuildingYOffset(BuildingTypeEnum type)
        {
            return type switch
            {
                BuildingTypeEnum.Wall => 1.0f,
                BuildingTypeEnum.Barracks => 1.5f,
                _ => 0.5f
            };
        }
    }
}
