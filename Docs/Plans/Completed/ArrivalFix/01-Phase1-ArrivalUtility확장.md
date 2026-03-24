# Phase 1: ArrivalUtility 확장

## 목표

`ArrivalUtility.cs`에 CellSize 마진 포함 도착 거리 메서드 + effectiveRadius 접근점 안전장치 메서드를 추가한다.

---

## 1-A. GetGridCompensatedArrivalDistance

FlowField 셀 양자화 + 이동 도착 허용 오차를 보정한 상호작용 도착 거리.

### primitive 오버로드

**위치**: `GetInteractionArrivalDistance(float, float)` 아래에 추가 (line 58 이후)

```csharp
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
```

### Lookup 오버로드

**위치**: `GetInteractionArrivalDistance(Entity, Entity, ...)` 아래에 추가 (line 74 이후)

```csharp
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
```

---

## 1-B. GetEffectiveRadius

그리드 차단 반경을 고려한 유효 반지름. 접근점이 blocked 셀 밖에 배치되도록 보장.

**위치**: `GetSafeArrivalRadius` 아래에 추가 (line 85 이후)

```csharp
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
```

> `in StructureFootprint` struct 파라미터 → `[BurstCompile]` 불가 (BC1064). `[MethodImpl(AggressiveInlining)]`만 사용.

### using 추가

파일 상단에 `using Shared;`는 이미 네임스페이스 내부이므로 추가 불필요. `StructureFootprint`는 `Shared` 네임스페이스에 있어 직접 접근 가능.

---

## 1-C. CalculateApproachPoint Lookup 오버로드 보강

기존 오버로드 (line 40-48):

```csharp
public static float3 CalculateApproachPoint(
    float3 fromPos, float3 targetPos,
    Entity targetEntity, in ComponentLookup<ObstacleRadius> radiusLookup,
    float margin = ApproachMargin)
{
    float targetRadius = radiusLookup.TryGetComponent(targetEntity, out var obs)
        ? obs.Radius : DefaultTargetRadius;
    return CalculateApproachPoint(fromPos, targetPos, targetRadius + margin);
}
```

**effectiveRadius 기반 오버로드 추가** (기존 오버로드 아래):

```csharp
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
```

---

## 변경 요약

| 메서드 | 어트리뷰트 | 파라미터 | 비고 |
|--------|-----------|---------|------|
| `GetGridCompensatedArrivalDistance(float, float, float)` | `[BurstCompile]` + `[AggressiveInlining]` | primitive only | Phase 2에서 사용 |
| `GetGridCompensatedArrivalDistance(Entity, Entity, Lookup, Lookup, float)` | `[AggressiveInlining]` | Entity/Lookup 포함 | Phase 2에서 사용 |
| `GetEffectiveRadius(float, in StructureFootprint, float)` | `[AggressiveInlining]` | struct 포함 → BC1064 방지 | Phase 3에서 사용 |
| `CalculateApproachPoint(..., footprintLookup, cellSize, ...)` | `[AggressiveInlining]` | 새 오버로드 | Phase 3에서 사용 |
