using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 도착 거리 공통 유틸리티 (Burst 호환 static 메서드)
    /// - 접근점 계산 (effectiveRadius 기반 오버로드 포함)
    /// - 상호작용 도착 거리 (그리드 이동 오차 보정 포함)
    /// - Dead Zone 없는 ArrivalRadius 계산
    /// - 그리드 차단 반경 기반 effectiveRadius 계산
    /// </summary>
    [BurstCompile]
    public static class ArrivalUtility
    {
        /// <summary>접근점 계산 시 타겟 표면에서의 여유 거리 (월드 단위, m)</summary>
        public const float ApproachMargin = 0.1f;
        /// <summary>타겟의 ObstacleRadius 정보가 없을 때 사용하는 기본 반지름 (월드 단위, m)</summary>
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
        /// FlowField 셀 양자화 + 이동 도착 허용 오차를 보정한 상호작용 도착 거리.
        /// 채집/건설 등 그리드 기반 이동 후 상호작용하는 모든 시스템에서 공통 사용.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [BurstCompile]
        public static float GetGridCompensatedArrivalDistance(
            float targetRadius, float interactionRange, float cellSize)
        {
            return targetRadius + interactionRange + cellSize;
        }

        /// <summary>
        /// ComponentLookup 기반 그리드 보정 상호작용 도착 거리
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetGridCompensatedArrivalDistance(
            Entity targetEntity, Entity unitEntity,
            in ComponentLookup<ObstacleRadius> radiusLookup,
            in ComponentLookup<WorkRange> workRangeLookup,
            float cellSize)
        {
            float targetRadius = radiusLookup.TryGetComponent(targetEntity, out var obs)
                ? obs.Radius : DefaultTargetRadius;
            float workRange = workRangeLookup.TryGetComponent(unitEntity, out var wr)
                ? wr.Value : 1.0f;
            return GetGridCompensatedArrivalDistance(targetRadius, workRange, cellSize);
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
        /// 그리드 차단 반경을 고려한 유효 반지름.
        /// 접근점이 blocked 셀 밖에 배치되도록 보장.
        /// 현재 모든 건물에서 ObstacleRadius >= gridBlockedHalfExtent이므로 실질적 변화 없음 (미래 대비).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetEffectiveRadius(
            float obstacleRadius, in StructureFootprint footprint, float cellSize)
        {
            int pathWidth = math.max(1, footprint.Width - 2);
            float gridBlockedHalfExtent = pathWidth * cellSize * 0.5f;
            return math.max(obstacleRadius, gridBlockedHalfExtent + cellSize * 0.5f);
        }

        /// <summary>
        /// effectiveRadius 기반 접근점 계산.
        /// StructureFootprint가 있으면 그리드 차단 반경을 고려한 유효 반지름 사용.
        /// 없으면 ObstacleRadius 그대로 사용 (기존 동작 유지).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 CalculateApproachPoint(
            float3 fromPos, float3 targetPos,
            Entity targetEntity,
            in ComponentLookup<ObstacleRadius> radiusLookup,
            in ComponentLookup<StructureFootprint> footprintLookup,
            float cellSize,
            float margin = ApproachMargin)
        {
            float targetRadius = radiusLookup.TryGetComponent(targetEntity, out var obs)
                ? obs.Radius : DefaultTargetRadius;

            if (footprintLookup.TryGetComponent(targetEntity, out var footprint))
            {
                targetRadius = GetEffectiveRadius(targetRadius, in footprint, cellSize);
            }

            return CalculateApproachPoint(fromPos, targetPos, targetRadius + margin);
        }

        /// <summary>
        /// AABB 표면까지의 XZ 2D 거리의 제곱. 유닛이 AABB 내부이면 0.
        /// 건설 도착 판정 등에서 중심 거리 대신 표면 거리 기반 판정에 사용.
        /// 비교 시 threshold도 제곱하여 sqrt 비용을 회피.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSqToAABBSurfaceXZ(
            float3 unitPos, float3 center, float halfW, float halfL)
        {
            float dx = math.max(0, math.abs(unitPos.x - center.x) - halfW);
            float dz = math.max(0, math.abs(unitPos.z - center.z) - halfL);
            return dx * dx + dz * dz;
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
