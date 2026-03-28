using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 군집 이동 격자 대형 유틸리티 (Burst 호환)
    /// </summary>
    [BurstCompile]
    public static class FormationUtility
    {
        /// <summary>
        /// 격자 대형 내 슬롯 오프셋 계산.
        /// N개 유닛을 sqrt(N) x sqrt(N) 격자로 배치, 이동 방향 기준 회전.
        /// </summary>
        /// <param name="slotIndex">유닛의 슬롯 인덱스 (0-based)</param>
        /// <param name="totalCount">그룹 내 총 유닛 수</param>
        /// <param name="spacing">유닛 간 간격 (m)</param>
        /// <param name="moveDir">이동 방향 (정규화된 XZ 벡터)</param>
        /// <returns>목적지에 더할 오프셋 (월드 좌표)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 CalculateFormationOffset(
            int slotIndex, int totalCount, float spacing, float3 moveDir)
        {
            if (totalCount <= 1) return float3.zero;

            int columns = (int)math.ceil(math.sqrt(totalCount));
            int rows = (totalCount + columns - 1) / columns;
            int col = slotIndex % columns;
            int row = slotIndex / columns;

            // 격자 중심 기준 오프셋 (로컬)
            float localX = (col - (columns - 1) * 0.5f) * spacing;
            float localZ = (row - (rows - 1) * 0.5f) * spacing;

            // 이동 방향 기준 회전
            // moveDir이 Z+ (전방), X+가 우측
            float3 forward = math.normalizesafe(new float3(moveDir.x, 0, moveDir.z));
            if (math.lengthsq(forward) < 0.001f)
                forward = new float3(0, 0, 1); // fallback

            float3 right = math.cross(math.up(), forward);

            return right * localX + forward * localZ;
        }
    }
}
