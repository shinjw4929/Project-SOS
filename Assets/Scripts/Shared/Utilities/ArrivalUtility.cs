using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 도착 거리 공통 유틸리티 (Burst 호환 static 메서드)
    /// - 접근점 계산, 상호작용 도착 거리, Dead Zone 없는 ArrivalRadius 계산
    /// </summary>
    [BurstCompile]
    public static class ArrivalUtility
    {
        public const float ApproachMargin = 0.1f;
        public const float DefaultTargetRadius = 1.5f;

        /// <summary>
        /// 접근점 계산: fromPos에서 targetPos 방향으로 standoffDistance만큼 떨어진 지점
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 CalculateApproachPoint(in float3 fromPos, in float3 targetPos, float standoffDistance)
        {
            float3 direction = targetPos - fromPos;
            float len = math.length(direction);

            if (len < 0.001f)
                return targetPos;

            direction /= len;
            return targetPos - direction * standoffDistance;
        }

        /// <summary>
        /// ComponentLookup 기반 접근점 계산 (standoff = targetRadius + margin)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 CalculateApproachPoint(
            float3 fromPos, float3 targetPos,
            Entity targetEntity, in ComponentLookup<ObstacleRadius> radiusLookup,
            float margin = ApproachMargin)
        {
            float targetRadius = radiusLookup.TryGetComponent(targetEntity, out var obs)
                ? obs.Radius : DefaultTargetRadius;
            return CalculateApproachPoint(fromPos, targetPos, targetRadius + margin);
        }

        /// <summary>
        /// 상호작용 도착 거리: targetRadius + interactionRange
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float GetInteractionArrivalDistance(float targetRadius, float interactionRange)
        {
            return targetRadius + interactionRange;
        }

        /// <summary>
        /// ComponentLookup 기반 상호작용 도착 거리
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetInteractionArrivalDistance(
            Entity targetEntity, Entity unitEntity,
            in ComponentLookup<ObstacleRadius> radiusLookup,
            in ComponentLookup<WorkRange> workRangeLookup)
        {
            float targetRadius = radiusLookup.TryGetComponent(targetEntity, out var obs)
                ? obs.Radius : DefaultTargetRadius;
            float workRange = workRangeLookup.TryGetComponent(unitEntity, out var wr)
                ? wr.Value : 1.0f;
            return GetInteractionArrivalDistance(targetRadius, workRange);
        }

        /// <summary>
        /// Dead Zone 없는 안전한 ArrivalRadius 계산
        /// 부등식: approachMargin + ArrivalRadius * 2 &lt;= interactionRange
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float GetSafeArrivalRadius(float interactionRange, float approachMargin = ApproachMargin)
        {
            return (interactionRange - approachMargin) * 0.5f;
        }

        /// <summary>
        /// 상호작용 범위 내 판정 (3D 거리)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWithinInteractionRange(in float3 unitPos, in float3 targetCenterPos, float arrivalDistance)
        {
            return math.distance(unitPos, targetCenterPos) <= arrivalDistance;
        }

        /// <summary>
        /// 상호작용 범위 내 판정 (XZ 2D 거리)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWithinInteractionRangeXZ(in float3 unitPos, in float3 targetCenterPos, float arrivalDistance)
        {
            float dist = math.distance(
                new float2(unitPos.x, unitPos.z),
                new float2(targetCenterPos.x, targetCenterPos.z)
            );
            return dist <= arrivalDistance;
        }
    }
}
