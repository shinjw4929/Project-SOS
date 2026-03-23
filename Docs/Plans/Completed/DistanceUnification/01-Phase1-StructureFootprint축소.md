# Phase 1: StructureFootprint 축소 + 그리드 단일 소스

**변경 파일**: 9개 (컴포넌트 2, Authoring 2, 시스템 4, 테스트 1)

> **핵심 원칙**: 그리드 셀 점유(Width/Length)가 건물 크기의 유일한 정의. PathWidth, WorldWidth, IsCircular 등은 모두 파생하거나 제거.

---

## 1-A. StructureFootprint 컴포넌트 수정

**파일**: `Assets/Scripts/Shared/Components/Stats/StructureFootprint.cs`

```csharp
// Before: 11 필드
public struct StructureFootprint : IComponentData
{
    public int Width;
    public int Length;
    public float Height;
    public int PathWidth;
    public int PathLength;
    public float WorldWidth;
    public float WorldLength;
    public float WorldHeight;
    public bool IsCircular;
    public float WorldRadius;
}

// After: 3 필드
public struct StructureFootprint : IComponentData
{
    /// <summary>배치 + 충돌 경계 (그리드 셀 단위, 0.5m/셀)</summary>
    public int Width;
    /// <summary>배치 + 충돌 경계 (그리드 셀 단위, 0.5m/셀)</summary>
    public int Length;
    /// <summary>건물 높이 (월드 단위). BuildArrivalSystem에서 위치 계산에 사용.</summary>
    public float Height;
}
```

**제거 필드와 대체:**

| 제거 필드 | 대체 방식 | 비고 |
|----------|----------|------|
| PathWidth | `math.max(1, Width - 2)` | 시스템에서 인라인 계산 |
| PathLength | `math.max(1, Length - 2)` | 동일 |
| WorldWidth | `Width * CellSize` | CellSize = 0.5f |
| WorldLength | `Length * CellSize` | 동일 |
| WorldHeight | 미사용 확인됨 | GridObstacleResponseSystem에서 미참조 |
| IsCircular | 제거 (모든 push-out 직사각형) | SC1 방식, ObstacleRadius가 원형 접근 담당 |
| WorldRadius | 제거 (IsCircular 제거로 불필요) | push-out은 Width×CellSize 직사각형 |

---

## 1-B. Authoring 수정

### StructureAuthoring

**파일**: `Assets/Scripts/Authoring/Entities/StructureAuthoring.cs`

```csharp
// Before: 11 필드
[Header("Grid Size")]
[Min(1)] public int width = 2;
[Min(1)] public int length = 2;
public float height = 1;
[Header("Pathfinding Size")]
[Min(1)] public int pathWidth = 2;
[Min(1)] public int pathLength = 2;
[Header("World Size")]
[Min(0.1f)] public float worldWidth = 1f;
[Min(0.1f)] public float worldLength = 1f;
[Min(0.1f)] public float navMeshHeight = 1f;
[Header("Shape")]
public bool isCircular = false;
[Min(0.1f)] public float navMeshRadius = 1f;
[Header("Interaction")]
[Min(0.1f)] public float interactionRadius = 1f;

// After: 4 필드
[Header("Grid Size (그리드 셀 단위, 0.5m/셀)")]
[Min(1)] public int width = 2;
[Min(1)] public int length = 2;
public float height = 1;

[Header("Obstacle Radius (상호작용/도착/공격 판정, 월드 단위)")]
[FormerlySerializedAs("interactionRadius")]
[Min(0.1f)] public float obstacleRadius = 1f;
```

**제거 필드**: pathWidth, pathLength, worldWidth, worldLength, navMeshHeight, isCircular, navMeshRadius (7개)

**Baker 수정**:
```csharp
// Before:
AddComponent(entity, new StructureFootprint
{
    Width = authoring.width,
    Length = authoring.length,
    Height = authoring.height,
    PathWidth = authoring.pathWidth,
    PathLength = authoring.pathLength,
    WorldWidth = authoring.worldWidth,
    WorldLength = authoring.worldLength,
    WorldHeight = authoring.navMeshHeight,
    IsCircular = authoring.isCircular,
    WorldRadius = authoring.navMeshRadius
});
AddComponent(entity, new ObstacleRadius { Radius = authoring.interactionRadius });

// After:
AddComponent(entity, new StructureFootprint
{
    Width = authoring.width,
    Length = authoring.length,
    Height = authoring.height
});
AddComponent(entity, new ObstacleRadius { Radius = authoring.obstacleRadius });
```

### ResourceNodeAuthoring

**파일**: `Assets/Scripts/Authoring/Entities/ResourceNodeAuthoring.cs`

동일한 변경 적용. `[FormerlySerializedAs("interactionRadius")]` 필수.

---

## 1-C. GridObstacleResponseSystem 수정

**파일**: `Assets/Scripts/Server/Systems/Movement/GridObstacleResponseSystem.cs`

### push-out 그리드 기반화

```csharp
// Before (line 77-86): IsCircular 분기
var fp = footprint.ValueRO;
float halfW, halfL;
if (fp.IsCircular)
{
    halfW = halfL = fp.WorldRadius;
}
else
{
    halfW = fp.WorldWidth * 0.5f;
    halfL = fp.WorldLength * 0.5f;
}

// After: 항상 직사각형
var fp = footprint.ValueRO;
float cellSize = gridSettings.CellSize;  // GridSettings 싱글톤에서 동적 참조
float halfW = fp.Width * cellSize * 0.5f;
float halfL = fp.Length * cellSize * 0.5f;
```

### BuildingPushInfo struct 수정

```csharp
// Before (line 11-16):
struct BuildingPushInfo
{
    public float3 Position;
    public float HalfW;
    public float HalfL;
    public bool IsCircular;
}

// After:
struct BuildingPushInfo
{
    public float3 Position;
    public float HalfW;
    public float HalfL;
}
```

### 밀어내기 로직 단순화 (line 117-166)

```csharp
// Before: IsCircular 분기로 원형/박스 충돌 판정
if (bld.IsCircular) { /* 원형 판정 */ }
else { /* 박스 판정 */ }

// After: 항상 박스 판정
bool isInside = math.abs(local.x) < bld.HalfW + entityR &&
                math.abs(local.z) < bld.HalfL + entityR;

if (isInside)
{
    float overlapX = (bld.HalfW + entityR) - math.abs(local.x);
    float overlapZ = (bld.HalfL + entityR) - math.abs(local.z);

    if (overlapX < overlapZ)
    {
        float sign = local.x >= 0 ? 1f : -1f;
        entityTransform.ValueRW.Position.x += sign * (overlapX + 0.1f);
    }
    else
    {
        float sign = local.z >= 0 ? 1f : -1f;
        entityTransform.ValueRW.Position.z += sign * (overlapZ + 0.1f);
    }
}
```

### GridObstacleCleanup 저장 수정 (line 65-70)

```csharp
// Before:
ecb.AddComponent(entity, new GridObstacleCleanup
{
    GridPosition = gridPos.ValueRO.Position,
    Width = fp.Width,
    Length = fp.Length,
    PathWidth = fp.PathWidth,
    PathLength = fp.PathLength
});

// After:
ecb.AddComponent(entity, new GridObstacleCleanup
{
    GridPosition = gridPos.ValueRO.Position,
    Width = fp.Width,
    Length = fp.Length
});
```

---

## 1-D. GridObstacleCleanup 컴포넌트 수정

**파일**: `Assets/Scripts/Shared/Components/Data/GridObstacleCleanup.cs`

```csharp
// Before:
public struct GridObstacleCleanup : ICleanupComponentData
{
    public int2 GridPosition;
    public int Width;
    public int Length;
    public int PathWidth;
    public int PathLength;
}

// After:
public struct GridObstacleCleanup : ICleanupComponentData
{
    public int2 GridPosition;
    public int Width;
    public int Length;
    // PathWidth/PathLength는 max(1, Width-2), max(1, Length-2)로 파생
}
```

---

## 1-E. PathWidth 파생 적용 (전체 호출자)

`GridUtility.MarkPathBlocked` / `UnmarkPathBlocked` 함수 시그니처는 변경 불필요 (pathWidth를 int 파라미터로 받음). 호출자만 변경.

### GridObstacleResponseSystem (line 56-60)

```csharp
// Before:
GridUtility.MarkPathBlocked(gridBuffer,
    gridPos.ValueRO.Position.x, gridPos.ValueRO.Position.y,
    footprint.ValueRO.Width, footprint.ValueRO.Length,
    footprint.ValueRO.PathWidth, footprint.ValueRO.PathLength,
    gridSizeX);

// After:
int pathWidth = math.max(1, footprint.ValueRO.Width - 2);
int pathLength = math.max(1, footprint.ValueRO.Length - 2);
GridUtility.MarkPathBlocked(gridBuffer,
    gridPos.ValueRO.Position.x, gridPos.ValueRO.Position.y,
    footprint.ValueRO.Width, footprint.ValueRO.Length,
    pathWidth, pathLength,
    gridSizeX);
```

### GridObstacleCleanupSystem (line 47-51)

```csharp
// Before:
GridUtility.UnmarkPathBlocked(gridBuffer,
    data.GridPosition.x, data.GridPosition.y,
    data.Width, data.Length,
    data.PathWidth, data.PathLength,
    gridSizeX);

// After:
int pathWidth = math.max(1, data.Width - 2);
int pathLength = math.max(1, data.Length - 2);
GridUtility.UnmarkPathBlocked(gridBuffer,
    data.GridPosition.x, data.GridPosition.y,
    data.Width, data.Length,
    pathWidth, pathLength,
    gridSizeX);
```

### ObstacleGridInitSystem (line 66-70)

```csharp
// Before:
GridUtility.MarkPathBlocked(
    gridBuffer, pos.x, pos.y,
    footprint.ValueRO.Width, footprint.ValueRO.Length,
    footprint.ValueRO.PathWidth, footprint.ValueRO.PathLength,
    gridSizeX);

// After:
int pathWidth = math.max(1, footprint.ValueRO.Width - 2);
int pathLength = math.max(1, footprint.ValueRO.Length - 2);
GridUtility.MarkPathBlocked(
    gridBuffer, pos.x, pos.y,
    footprint.ValueRO.Width, footprint.ValueRO.Length,
    pathWidth, pathLength,
    gridSizeX);
```

### InitialWallDecaySystem (line 67-70)

```csharp
// Before:
GridUtility.UnmarkPathBlocked(gridBuffer,
    pos.x, pos.y, w, l,
    footprint.ValueRO.PathWidth, footprint.ValueRO.PathLength,
    gridSizeX);

// After:
int pathWidth = math.max(1, w - 2);
int pathLength = math.max(1, l - 2);
GridUtility.UnmarkPathBlocked(gridBuffer,
    pos.x, pos.y, w, l,
    pathWidth, pathLength,
    gridSizeX);
```

### FlowFieldGridUtilityTests (7개 호출)

**파일**: `Assets/Tests/EditMode/Utilities/FlowFieldGridUtilityTests.cs`

기존 테스트에서 pathWidth/pathLength를 하드코딩으로 전달. 리팩토링 후에도 동일한 int 파라미터이므로 테스트 값만 `max(1, width-2)` 규칙에 맞게 확인/수정.

---

## 1-F. CellSize 0.5m → 1.0m 전환

CellSize를 1.0m으로 변경. Width/Length 값은 그대로 유지하여 건물 물리 크기가 2배로 확대.

| 프리팹 | Width | 물리 크기 (Before 0.5m) | 물리 크기 (After 1.0m) | PathWidth |
|--------|-------|----------------------|---------------------|-----------|
| Wall | 4 | 2m×2m | **4m×4m** | 2 |
| Barracks | 6 | 3m×3m | **6m×6m** | 4 |
| ResourceCenter | 8 | 4m×4m | **8m×8m** | 6 |
| OreVein | 8 | 4m×4m | **8m×8m** | 6 |

**변경 대상:**
- `GridSettingsAuthoring.cs`: `cellSize` 기본값 0.5f → **1.0f**
- `GridSettingsAuthoring.cs`: `buildSnapCells` 2 → **1** (1m 스냅 유지. 2×1.0=2m 방지)
- `GridUtility.cs`: `ResourceNodeExclusionDistance` 18 → **9** (9m 거리 유지. 18×1.0=18m 방지)
- `BuildArrivalSystem.cs` line 114: `+ 0.5f` → `+ gridSettings.CellSize * 0.7f` (셀 대각선 오차 보상을 CellSize 비례로)
- 프리팹 Width/Length: **변경 없음** (동일 셀 수)
- ObstacleRadius, SpatialHash 셀 크기(10m/3m): **변경 없음** (그리드 독립)
- 시스템 내 `* 0.5f` (절반 계산): **변경 없음** (CellSize와 무관한 산술 상수)

**장점**: FlowField BFS 4배 빠름 (40,000→10,000 셀), 캐시 메모리 4배 절감, Width=셀수=미터수로 직관적.

## 1-G. 프리팹 비주얼 모델 조정 (사용자 수동)

CellSize 1.0m 전환 + push-out 그리드 기반화로 건물 물리 크기가 변경. 비주얼 모델을 새 크기에 맞게 수동 조정 필요:

| 프리팹 | 신규 물리 크기 | Push 경계 (Width×1.0/2) |
|--------|-------------|----------------------|
| Wall | 4m×4m | ±2.0m |
| Barracks | 6m×6m | ±3.0m |
| ResourceCenter | 8m×8m | ±4.0m |
| OreVein | 8m×8m | ±4.0m |

---

## 체크리스트

- [ ] `StructureFootprint.cs`: 8개 필드 제거 (PathWidth, PathLength, WorldWidth, WorldLength, WorldHeight, WorldRadius, IsCircular)
- [ ] `GridObstacleCleanup.cs`: PathWidth, PathLength 필드 제거
- [ ] `StructureAuthoring.cs`: 7개 필드 제거 + `interactionRadius` → `obstacleRadius` + Baker 수정
- [ ] `ResourceNodeAuthoring.cs`: 동일
- [ ] `GridObstacleResponseSystem.cs`: IsCircular 분기 제거 + Width×CellSize 직사각형 + BuildingPushInfo 수정 + GridObstacleCleanup 저장 수정
- [ ] `GridObstacleCleanupSystem.cs`: `data.PathWidth` → `max(1, data.Width - 2)` 파생
- [ ] `ObstacleGridInitSystem.cs`: `fp.PathWidth` → `max(1, fp.Width - 2)` 파생
- [ ] `InitialWallDecaySystem.cs`: `fp.PathWidth` → `max(1, fp.Width - 2)` 파생
- [ ] `FlowFieldGridUtilityTests.cs`: pathWidth 하드코딩 값 수정 (7개 호출)
- [ ] `GridSettingsAuthoring.cs`: cellSize 0.5f → 1.0f
- [ ] `GridSettingsAuthoring.cs`: buildSnapCells 2 → 1 (1m 스냅 유지)
- [ ] `GridUtility.cs`: ResourceNodeExclusionDistance 18 → 9
- [ ] `BuildArrivalSystem.cs`: `+ 0.5f` → `+ gridSettings.CellSize * 0.7f` (line 114, 셀 오차 보상 동적화)
- [ ] `FlowFieldGridUtilityTests.cs`: CellSize 0.5f → 1.0f (line 23) + 테스트 기대값 0.25f → 0.5f (line 45, 48 등 CellSize 파생값)
- [ ] `WanderUtilityTests.cs`: CellSize 0.5f → 1.0f (line 223)
- [ ] `GridUtilityTests.cs`: `Assert.AreEqual(18, ResourceNodeExclusionDistance)` → 9 (line 389)
- [ ] Authoring Header/Tooltip 주석: "0.5m" → "1.0m" (StructureAuthoring, ResourceNodeAuthoring, GridSettingsAuthoring, GridSettings)
- [ ] 프리팹 비주얼 수동 조정 (사용자)
- [ ] Burst 빌드 확인
