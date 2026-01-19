using Unity.Burst;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 공간 분할 해시 유틸리티
    /// <para>- 공용 셀 크기 상수 정의</para>
    /// <para>- 해시 계산 함수 (중앙 집중화)</para>
    /// <para>- 대형 유닛 AABB 셀 범위 계산</para>
    /// </summary>
    [BurstCompile]
    public static class SpatialHashUtility
    {
        /// <summary>타겟팅용 셀 크기 (적→아군, 유닛→적)</summary>
        public const float TargetingCellSize = 10.0f;

        /// <summary>이동/충돌 회피용 셀 크기</summary>
        public const float MovementCellSize = 3.0f;

        /// <summary>해시 충돌 방지 여유 계수</summary>
        public const float CapacityMultiplier = 1.5f;

        /// <summary>
        /// 위치 기반 셀 해시 계산
        /// </summary>
        [BurstCompile]
        public static int GetCellHash(in float3 pos, float cellSize)
        {
            return (int)math.hash(new int2(
                (int)math.floor(pos.x / cellSize),
                (int)math.floor(pos.z / cellSize)
            ));
        }

        /// <summary>
        /// 오프셋 적용 셀 해시 계산 (인접 셀 탐색용)
        /// </summary>
        [BurstCompile]
        public static int GetCellHash(in float3 pos, int xOff, int zOff, float cellSize)
        {
            return (int)math.hash(new int2(
                (int)math.floor(pos.x / cellSize) + xOff,
                (int)math.floor(pos.z / cellSize) + zOff
            ));
        }

        /// <summary>
        /// 좌표 기반 해시 계산
        /// </summary>
        [BurstCompile]
        public static int GetHashFromCoords(in int2 coords)
        {
            return (int)math.hash(coords);
        }

        /// <summary>
        /// 대형 유닛 AABB 셀 범위 계산
        /// <para>radius > cellSize * 0.5f인 경우 여러 셀에 등록 필요</para>
        /// </summary>
        [BurstCompile]
        public static void GetCellRange(in float3 pos, float radius, float cellSize,
                                         out int2 minCell, out int2 maxCell)
        {
            minCell = new int2(
                (int)math.floor((pos.x - radius) / cellSize),
                (int)math.floor((pos.z - radius) / cellSize)
            );
            maxCell = new int2(
                (int)math.floor((pos.x + radius) / cellSize),
                (int)math.floor((pos.z + radius) / cellSize)
            );
        }

        /// <summary>
        /// 대형 유닛 여부 판단 (AABB 다중 셀 등록 필요 여부)
        /// </summary>
        [BurstCompile]
        public static bool IsLargeEntity(float radius, float cellSize)
        {
            return radius > cellSize * 0.5f;
        }
    }
}
