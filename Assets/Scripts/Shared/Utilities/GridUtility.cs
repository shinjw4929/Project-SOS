using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public static class GridUtility
    {
        // 반복 호출되는 작은 함수들의 오버헤드 제거
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 WorldToGrid(float3 worldPos, GridSettings settings)
        {
            // float2 연산으로 간소화 (XZ 평면 기준)
            float2 localPos = worldPos.xz - settings.GridOrigin;
            return (int2)math.floor(localPos / settings.CellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 WorldToGridForBuilding(float3 centerPos, int width, int length, GridSettings settings)
        {
            // 중심점 -> 좌하단 코너로 변환
            // (width, length)를 float2로 변환하여 벡터 연산 수행
            float2 sizeOffset = new float2(width, length) * settings.CellSize * 0.5f;
            float2 cornerPos = centerPos.xz - sizeOffset;

            // 좌표 정규화
            return (int2)math.round((cornerPos - settings.GridOrigin) / settings.CellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GridToWorld(int gridX, int gridY, int width, int length, GridSettings settings)
        {
            // (gridX, gridY)는 좌하단 기준, 여기에 건물의 반너비/반높이를 더해 중심점 계산
            float2 gridSize = new float2(width, length) * settings.CellSize;
            float2 gridPos = new float2(gridX, gridY) * settings.CellSize;
            
            float2 centerPos = settings.GridOrigin + gridPos + (gridSize * 0.5f);

            return new float3(centerPos.x, 0, centerPos.y);
        }

        // 3D Y축 회전 등을 고려하지 않은 단순 2D AABB
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOverlapping(int2 pos1, int2 size1, int2 pos2, int2 size2)
        {
            return pos1.x < pos2.x + size2.x &&
                   pos1.x + size1.x > pos2.x &&
                   pos1.y < pos2.y + size2.y &&
                   pos1.y + size1.y > pos2.y;
        }

        /// <summary>
        /// 그리드 영역 점유 확인 (최적화됨)
        /// </summary>
        public static bool IsOccupied(DynamicBuffer<GridCell> buffer, int startX, int startY, int width, int length, int gridSizeX, int gridSizeY)
        {
            // 1. [최적화] 전체 건물이 맵 범위를 벗어나는지 먼저 검사 (Loop 밖에서 처리)
            if (startX < 0 || startY < 0 || startX + width > gridSizeX || startY + length > gridSizeY)
            {
                return true; // 맵 밖은 건설 불가
            }

            // 2. 점유 검사
            // 메모리 접근 패턴(Cache Locality)을 고려하여 Y(Row) -> X(Col) 순서가 일반적이지만,
            // 2D 배열을 1D로 펼친 경우 인덱스 계산 순서에 따름 (여기서는 Z가 행 역할)
            for (int z = 0; z < length; z++)
            {
                // 행(Row) 시작 인덱스 미리 계산
                int rowIndex = (startY + z) * gridSizeX; 
                
                for (int x = 0; x < width; x++)
                {
                    int index = rowIndex + (startX + x);
                    
                    // 버퍼 범위 안전 장치 (위의 경계 검사가 통과했다면 사실상 불필요하지만 안전을 위해 유지 가능)
                    if (buffer[index].IsOccupied)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 그리드 점유 마킹 (최적화됨)
        /// </summary>
        public static void MarkOccupied(DynamicBuffer<GridCell> buffer, int startX, int startY, int width, int length, int gridSizeX)
        {
            // 범위 검사 (필요하다면 추가, 보통은 IsOccupied 통과 후 호출되므로 생략하거나 Assert)
            if (startX < 0 || startY < 0) return; 

            for (int z = 0; z < length; z++)
            {
                int rowIndex = (startY + z) * gridSizeX;
                for (int x = 0; x < width; x++)
                {
                    int index = rowIndex + (startX + x);
                    
                    if (index < buffer.Length)
                    {
                        // 구조체 전체를 새로 생성하지 않고 내부 값만 변경하는 것이
                        // 컴파일러 최적화에 유리할 수 있음 (ref 사용 가능 시)
                        // DynamicBuffer는 ref 리턴을 지원하므로 아래 방식 권장
                        var cell = buffer[index];
                        cell.IsOccupied = true;
                        buffer[index] = cell;
                    }
                }
            }
        }
        
        // [편의성 오버로드] IsOccupied 마킹 해제용 (건물 파괴 시 등)
        public static void UnmarkOccupied(DynamicBuffer<GridCell> buffer, int startX, int startY, int width, int length, int gridSizeX)
        {
            for (int z = 0; z < length; z++)
            {
                int rowIndex = (startY + z) * gridSizeX;
                for (int x = 0; x < width; x++)
                {
                    int index = rowIndex + (startX + x);
                    if (index < buffer.Length)
                    {
                        var cell = buffer[index];
                        cell.IsOccupied = false;
                        buffer[index] = cell;
                    }
                }
            }
        }
    }
}