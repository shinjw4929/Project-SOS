using Unity.Entities;
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
        /// 건물 중심점에서 원래 그리드 좌표를 역산 (GridToWorld의 역함수)
        /// GridToWorld는 건물 중심점을 반환하므로, 이를 역산하여 원래 그리드 좌표를 구함
        /// </summary>
        public static int2 WorldToGridForBuilding(float3 centerPos, int width, int height, GridSettings settings)
        {
            // 중심점에서 건물 크기의 절반을 빼서 좌하단 모서리로 변환
            float cornerX = centerPos.x - (width * settings.cellSize) / 2f;
            float cornerZ = centerPos.z - (height * settings.cellSize) / 2f;

            return new int2(
                (int)math.round((cornerX - settings.gridOrigin.x) / settings.cellSize),
                (int)math.round((cornerZ - settings.gridOrigin.y) / settings.cellSize)
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

        /// <summary>
        /// 그리드 영역이 점유되었는지 확인
        /// </summary>
        public static bool IsOccupied(DynamicBuffer<GridCell> buffer, int x, int y, int width, int height, int gridWidth, int gridHeight)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int cx = x + dx;
                    int cy = y + dy;
                    if (cx < 0 || cx >= gridWidth || cy < 0 || cy >= gridHeight)
                        return true;  // 범위 밖 = 배치 불가
                    int index = cy * gridWidth + cx;
                    if (buffer[index].isOccupied)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 그리드 영역을 점유로 마킹
        /// </summary>
        public static void MarkOccupied(DynamicBuffer<GridCell> buffer, int x, int y, int width, int height, int gridWidth)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int index = (y + dy) * gridWidth + (x + dx);
                    if (index >= 0 && index < buffer.Length)
                        buffer[index] = new GridCell { isOccupied = true };
                }
            }
        }
    }
}
