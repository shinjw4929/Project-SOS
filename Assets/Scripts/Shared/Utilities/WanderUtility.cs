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
        public const float DefaultStuckCheckInterval = 3.0f;
        public const float DefaultStuckThreshold = 2.0f;
        public const float DefaultDormantMinDuration = 5.0f;
        public const float DefaultDormantMaxDuration = 8.0f;

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
        public static bool CheckStuck(
            in float3 currentPos,
            in float3 lastCheckPos,
            float lastCheckTime,
            float elapsedTime,
            out bool isStuck,
            float stuckCheckInterval = DefaultStuckCheckInterval,
            float stuckThreshold = DefaultStuckThreshold)
        {
            isStuck = false;
            if (elapsedTime - lastCheckTime < stuckCheckInterval)
                return false;

            float movedDistance = math.distance(currentPos, lastCheckPos);
            isStuck = movedDistance < stuckThreshold;
            return true;
        }

        /// <summary>
        /// Dormant 깨어남 시간 계산 (dormantMin~dormantMax 랜덤)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CalculateDormantWakeTime(
            int entityIndex, float elapsedTime,
            float dormantMin = DefaultDormantMinDuration,
            float dormantMax = DefaultDormantMaxDuration)
        {
            uint seed = (uint)entityIndex ^ (uint)(elapsedTime * 1000f) ^ 0xDEADBEEF;
            var random = Random.CreateFromIndex(seed);
            return elapsedTime + random.NextFloat(dormantMin, dormantMax);
        }

        public const float DefaultWanderBiasFactor = 0.5f;
        public const float DefaultWanderMaxDistance = 40.0f;

        /// <summary>
        /// 편향 배회 목적지 생성.
        /// biasFactor=0: 순수 랜덤 방향, biasFactor=1: biasTarget 방향 직진.
        /// biasTarget: 편향 대상 좌표 (아군 유닛 위치 등). default(float3)이면 맵 중심 사용.
        /// wanderMaxDistance: 현재 위치에서 최대 이동 거리.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenerateWanderDestination(
            int entityIndex,
            uint frameCount,
            float elapsedTime,
            in float3 currentPos,
            in GridSettings gridSettings,
            out float3 result,
            float biasFactor = DefaultWanderBiasFactor,
            float wanderMaxDistance = DefaultWanderMaxDistance,
            float3 biasTarget = default)
        {
            uint seed = (uint)entityIndex ^ (frameCount * 0x9E3779B9) ^ (uint)(elapsedTime * 1000);
            var random = Random.CreateFromIndex(seed);

            float2 mapMin = gridSettings.GridOrigin;
            float2 mapSize = new float2(
                gridSettings.GridSize.x * gridSettings.CellSize,
                gridSettings.GridSize.y * gridSettings.CellSize);

            // biasTarget이 zero이면 맵 중심 사용
            if (math.lengthsq(biasTarget) < 0.001f)
            {
                float2 mapCenter = mapMin + mapSize * 0.5f;
                biasTarget = new float3(mapCenter.x, currentPos.y, mapCenter.y);
            }

            // 랜덤 방향 생성
            float angle = random.NextFloat(0f, math.PI * 2f);
            float3 randomDir = new float3(math.cos(angle), 0f, math.sin(angle));

            // 편향 대상 방향 (현재 위치 → biasTarget)
            float3 toBias = new float3(biasTarget.x - currentPos.x, 0f, biasTarget.z - currentPos.z);
            float3 toBiasDir = math.normalizesafe(toBias);

            // 편향 방향 블렌딩
            float3 biasedDir = math.normalizesafe(math.lerp(randomDir, toBiasDir, biasFactor));

            // 거리 결정
            float dist = random.NextFloat(wanderMaxDistance * 0.5f, wanderMaxDistance);

            // 목적지 = 현재 위치 + 편향 방향 * 거리, 맵 범위 내 클램프
            result = new float3(
                math.clamp(currentPos.x + biasedDir.x * dist, mapMin.x + 5f, mapMin.x + mapSize.x - 5f),
                currentPos.y,
                math.clamp(currentPos.z + biasedDir.z * dist, mapMin.y + 5f, mapMin.y + mapSize.y - 5f));
        }
    }
}
