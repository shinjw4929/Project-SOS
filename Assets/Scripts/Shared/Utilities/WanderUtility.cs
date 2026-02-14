using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 적 배회(Wander) 공통 유틸리티 (Burst 호환 static 메서드)
    /// - EnemyTargetJob, EnemyWanderOnlyJob의 중복 로직 추출
    /// </summary>
    [BurstCompile]
    public static class WanderUtility
    {
        // 위치 정체 체크 간격 (초)
        public const float StuckCheckInterval = 3.0f;
        // stuck 판정 이동 거리 (미터)
        public const float StuckThreshold = 2.0f;
        // Dormant 최소/최대 지속 시간 (초)
        public const float DormantMinDuration = 5.0f;
        public const float DormantMaxDuration = 8.0f;

        /// <summary>
        /// Stuck 감지 판정: 일정 시간 동안 이동 거리가 임계치 미만이면 stuck
        /// </summary>
        /// <param name="currentPos">현재 위치</param>
        /// <param name="lastCheckPos">마지막 체크 위치</param>
        /// <param name="lastCheckTime">마지막 체크 시간</param>
        /// <param name="elapsedTime">현재 경과 시간</param>
        /// <param name="isStuck">stuck 여부 (out)</param>
        /// <returns>체크가 수행되었는지 (시간 간격 충족 여부)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static bool CheckStuck(
            in float3 currentPos,
            in float3 lastCheckPos,
            float lastCheckTime,
            float elapsedTime,
            out bool isStuck)
        {
            isStuck = false;
            if (elapsedTime - lastCheckTime < StuckCheckInterval)
                return false;

            float movedDistance = math.distance(currentPos, lastCheckPos);
            isStuck = movedDistance < StuckThreshold;
            return true;
        }

        /// <summary>
        /// Dormant 깨어남 시간 계산 (5~8초 랜덤)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float CalculateDormantWakeTime(int entityIndex, float elapsedTime)
        {
            uint seed = (uint)entityIndex ^ (uint)(elapsedTime * 1000f) ^ 0xDEADBEEF;
            var random = Random.CreateFromIndex(seed);
            return elapsedTime + random.NextFloat(DormantMinDuration, DormantMaxDuration);
        }

        /// <summary>
        /// 랜덤 배회 목적지 생성
        /// </summary>
        [BurstCompile]
        public static void GenerateWanderDestination(
            int entityIndex,
            uint frameCount,
            float elapsedTime,
            float currentY,
            in GridSettings gridSettings,
            out float3 result)
        {
            uint seed = (uint)entityIndex ^ (frameCount * 0x9E3779B9) ^ (uint)(elapsedTime * 1000);
            var random = Random.CreateFromIndex(seed);

            float2 mapMin = gridSettings.GridOrigin;
            float2 mapMax = mapMin + new float2(
                gridSettings.GridSize.x * gridSettings.CellSize,
                gridSettings.GridSize.y * gridSettings.CellSize);

            result = new float3(
                random.NextFloat(mapMin.x + 5f, mapMax.x - 5f),
                currentY,
                random.NextFloat(mapMin.y + 5f, mapMax.y - 5f));
        }
    }
}
