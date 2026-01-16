using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 이동 계산 유틸리티 (순수 함수)
    /// PredictedMovementSystem에서 사용하는 핵심 수식을 테스트 가능한 형태로 추출
    /// </summary>
    [BurstCompile]
    public static class MovementMath
    {
        /// <summary>
        /// 감속 시작 거리 계산 (등가속도 운동 공식: v² = 2as → s = v²/(2a))
        /// </summary>
        /// <param name="maxSpeed">최대 속도</param>
        /// <param name="deceleration">감속도</param>
        /// <returns>감속을 시작해야 하는 거리</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float CalculateSlowingDistance(float maxSpeed, float deceleration)
        {
            if (deceleration <= 0f) return 0f;
            return (maxSpeed * maxSpeed) / (2f * deceleration);
        }

        /// <summary>
        /// 목표 속도 계산 (Arrival 감속 로직)
        /// </summary>
        /// <param name="distance">목표까지의 거리</param>
        /// <param name="maxSpeed">최대 속도</param>
        /// <param name="slowingDistance">감속 시작 거리</param>
        /// <param name="hasNextWaypoint">다음 웨이포인트가 있는지 (있으면 감속 안함)</param>
        /// <returns>목표 속도</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float CalculateTargetSpeed(float distance, float maxSpeed, float slowingDistance, bool hasNextWaypoint)
        {
            // 다음 웨이포인트가 있으면 최대 속도 유지 (감속 안함)
            if (hasNextWaypoint) return maxSpeed;

            // 감속 구간 밖이면 최대 속도
            if (distance >= slowingDistance) return maxSpeed;

            // 거리에 비례하여 속도 감소 (선형 감속)
            float targetSpeed = maxSpeed * (distance / slowingDistance);

            // 최소 속도 보장 (너무 느리면 도착 못함)
            return math.max(targetSpeed, MinSpeed);
        }

        /// <summary>
        /// 새 속도 계산 (가속/감속 적용)
        /// </summary>
        /// <param name="currentSpeed">현재 속도</param>
        /// <param name="targetSpeed">목표 속도</param>
        /// <param name="acceleration">가속도</param>
        /// <param name="deceleration">감속도</param>
        /// <param name="deltaTime">시간 간격</param>
        /// <returns>새 속도</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float CalculateNewSpeed(float currentSpeed, float targetSpeed, float acceleration, float deceleration, float deltaTime)
        {
            float speedDiff = targetSpeed - currentSpeed;

            // 가속 또는 감속 선택
            float accelToUse = speedDiff > 0f ? acceleration : deceleration;

            // 오버슈팅 방지: 변화량이 목표-현재 차이보다 크면 클램프
            float maxChange = accelToUse * deltaTime;
            float actualChange = math.sign(speedDiff) * math.min(math.abs(speedDiff), maxChange);

            float newSpeed = currentSpeed + actualChange;

            // 음수 속도 방지
            return math.max(0f, newSpeed);
        }

        /// <summary>
        /// 도착 판정 (거리 기반)
        /// </summary>
        /// <param name="distance">목표까지의 거리</param>
        /// <param name="arrivalThreshold">도착 판정 거리</param>
        /// <param name="hasNextWaypoint">다음 웨이포인트가 있는지</param>
        /// <returns>도착 여부</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static bool IsAtArrivalThreshold(float distance, float arrivalThreshold, bool hasNextWaypoint)
        {
            // 다음 웨이포인트가 있으면 도착 아님 (중간 지점)
            if (hasNextWaypoint) return false;

            return distance < arrivalThreshold;
        }

        /// <summary>
        /// 정지 보정 필요 여부 (진동 방지)
        /// </summary>
        /// <param name="distance">목표까지의 거리</param>
        /// <param name="currentSpeed">현재 속도</param>
        /// <param name="hasNextWaypoint">다음 웨이포인트가 있는지</param>
        /// <returns>완전 정지 필요 여부</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static bool ShouldSnapToStop(float distance, float currentSpeed, bool hasNextWaypoint)
        {
            if (hasNextWaypoint) return false;
            return distance < SnapDistance && currentSpeed < SnapSpeedThreshold;
        }

        /// <summary>
        /// 코너링 (웨이포인트 스위칭) 필요 여부
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static bool ShouldSwitchWaypoint(float distance, bool hasNextWaypoint)
        {
            return hasNextWaypoint && distance < CornerRadius;
        }

        // 상수 정의
        public const float MinSpeed = 0.5f;
        public const float ArrivalThreshold = 0.3f;
        public const float SnapDistance = 0.02f;
        public const float SnapSpeedThreshold = 0.3f;
        public const float CornerRadius = 0.5f;
    }
}
