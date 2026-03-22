# Phase 4: Authoring/컴포넌트 정리

---

## 그리드 설정 변경

### GridSettingsAuthoring.cs
- `cellSize` 기본값: **1.0f → 0.5f**
- `gridSize`는 `groundTransform`에서 자동 계산되므로 수동 변경 불필요
  - 기존 100m 맵: `scale * 10f / 0.5f` = 200×200 셀 (자동)

---

## 컴포넌트 변경

### GridCell (기존 수정)

```csharp
// Shared/Buffers/GridCell.cs
public struct GridCell : IBufferElementData
{
    public byte IsOccupied;      // 건물 배치 점유 (0=비점유, 1=점유)
    public byte IsPathBlocked;   // 경로탐색 차단 (0=통과 가능, 1=차단)
}
```

> **bool 대신 byte**: GridSettingsAuthoring의 `Reinterpret<byte>` + `MemSet(0)` 초기화와 호환. bool은 컴파일러 레이아웃(패딩)에 의해 `sizeof(GridCell)`이 예측 불가하므로 byte로 크기를 정확히 2 byte로 고정.

### StructureFootprint (기존 수정)

```csharp
// Shared/Components/Stats/StructureFootprint.cs
public struct StructureFootprint : IComponentData
{
    // 그리드 칸 수 — 건설/점유 시스템용 (배치 풋프린트)
    public int Width;
    public int Length;
    public float Height;

    // 경로탐색 풋프린트 — FlowField passability용
    // 배치 풋프린트의 중앙에 위치 (오프셋 자동 계산)
    // 벽: PathWidth < Width (반투과), 나머지: PathWidth = Width (완전 차단)
    public int PathWidth;
    public int PathLength;

    // GridObstacle 밀어내기용 실제 월드 크기
    public float WorldWidth;
    public float WorldLength;
    public float WorldHeight;

    // 원형 장애물 지원 (GridObstacle 밀어내기)
    public bool IsCircular;
    public float WorldRadius;
}
```

### 구조물 풋프린트 값 (0.5m 셀 기준)

| 구조물 | Width×Length | PathWidth×PathLength | 물리 크기 | 비고 |
|--------|------------|---------------------|----------|------|
| Wall | 4×4 | **2×2** | 2m × 2m | 반투과 |
| Barracks | 6×6 | **6×6** | 3m × 3m | 완전 차단 |
| Turret | (프리팹 크기 확인) | (Width와 동일) | (확인 필요) | 완전 차단 |
| ResourceCenter | 8×8 | **8×8** | 4m × 4m | 완전 차단 |

> **Turret**: StructureAuthoring에 타입이 정의되어 있고 NeedsNavMeshObstacle을 받으므로 풋프린트 설정 필수. 프리팹이 존재하면 실제 크기에 맞게 설정. pathWidth/pathLength 기본값은 width/length와 동일(완전 차단).

---

## StructureAuthoring 변경

```csharp
// 기존 필드 (Width/Length 값 변경)
[Header("Grid Size (그리드 칸 수, 0.5m 셀 기준)")]
[Min(1)] public int width = 2;
[Min(1)] public int length = 2;

// 신규 필드
[Header("Pathfinding Size (경로탐색 점유, 0.5m 셀 기준)")]
[Tooltip("배치 풋프린트 중앙 영역만 경로를 차단. 벽: 배치보다 작게 설정하여 소형 유닛 통과 허용")]
[Min(1)] public int pathWidth = 2;
[Min(1)] public int pathLength = 2;
```

> **pathWidth/pathLength 기본값**: width/length와 동일하게 설정하여 미설정 시 완전 차단(안전 기본값). 벽만 인스펙터에서 명시적으로 축소.

Baker 변경:
```csharp
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
```

### 프리팹별 인스펙터 값

| 프리팹 | width | length | pathWidth | pathLength |
|--------|-------|--------|-----------|------------|
| Wall | 4 | 4 | **2** | **2** |
| Barracks | 6 | 6 | **6** | **6** |
| Turret | (확인) | (확인) | (=width) | (=length) |
| ResourceCenter | 8 | 8 | **8** | **8** |

---

## MovementAuthoring 변경

- `AgentTypeIndex` → `PathfindingSize` (Small/Large)
- `GridPathfindingSize` + `FlowFieldRef(Key=-1)` 베이킹
- `AddBuffer<PathWaypoint>` + `NavMeshAgentConfig` 베이킹 제거
- `CurrentWaypointIndex = 0` 초기화 제거

---

## 신규 컴포넌트

| 파일 | 타입 | 내용 |
|------|------|------|
| `Shared/Components/Movement/GridPathfindingSize.cs` | IComponentData | `byte CellPadding` (0=Small, 1=Large) |
| `Shared/Components/Movement/FlowFieldRef.cs` | IComponentData | `int Key` (캐시 조회 키, 베이킹 시 -1) |
| `Shared/Components/Data/GridObstacleCleanup.cs` | ICleanupComponentData | `int2 GridPosition`, `int Width`, `int Length`, `int PathWidth`, `int PathLength` |

---

## 제거 파일

| 파일 | 이유 |
|------|------|
| `Shared/Components/Movement/NavMeshAgentConfig.cs` | GridPathfindingSize로 대체 |
| `Shared/Buffers/PathWaypoint.cs` | Flow Field 직접 스티어링으로 불필요 |
| `Server/Utilities/NavMeshPathUtils.cs` | Funnel 알고리즘 불필요 |

---

## 기존 시스템 수정

### ObstacleGridInitSystem.cs

초기화 시스템이므로 IsOccupied + IsPathBlocked **모두 마킹** (1회성, 책임 분리 예외).

```csharp
// MarkOccupied: 기존 시그니처 유지 (int2 startPos)
GridUtility.MarkOccupied(gridBuffer, gridPos, footprint.Width, footprint.Length, gridSizeX);
// MarkPathBlocked: 중앙 오프셋 계산을 위해 배치/경로탐색 크기 모두 전달
GridUtility.MarkPathBlocked(gridBuffer, gridPos,
    footprint.Width, footprint.Length, footprint.PathWidth, footprint.PathLength, gridSizeX);
```

NavMesh 참조 제거.

### GridOccupancyEventSystem.cs

**IsOccupied만 담당** (기존 동작 유지, IsPathBlocked 관련 변경 없음).

- `AddOccupancyJob`: `MarkOccupied` (기존 그대로)
- `RemoveOccupancyJob`: `UnmarkOccupied` (기존 그대로)
- IsPathBlocked 마킹/해제는 GridObstacleResponseSystem/GridObstacleCleanupSystem이 담당 (Phase 3)

### MovementGoal.cs

- `CurrentWaypointIndex`, `TotalWaypoints` 필드 제거
- `IsPathDirty`, `IsPathPartial`에 `[MarshalAs(UnmanagedType.U1)]` 추가
- `[GhostComponent]` 유지

### NavMeshObstacleProxy.cs

- `NavMeshObstacleReference` class 제거
- `NeedsNavMeshObstacle` struct 유지

---

## CurrentWaypointIndex 참조 제거 (6개 시스템, 7곳)

| 파일 | 변경 |
|------|------|
| `Server/Systems/Combat/MeleeAttackSystem.cs` | `CurrentWaypointIndex = 0` 제거 (148행) |
| `Server/Systems/Combat/RangedAttackSystem.cs` | `CurrentWaypointIndex = 0` 제거 (**2곳**: 183행, 261행) |
| `Server/Systems/Commands/Combat/HandleAttackRequestSystem.cs` | `CurrentWaypointIndex = 0` 제거 (146행) |
| `Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` | `CurrentWaypointIndex = 0` 제거 (107행) |
| `Server/Systems/Commands/Construction/HandleBuildMoveRequestSystem.cs` | `CurrentWaypointIndex = 0` 제거 (151행) |
| `Server/Systems/Commands/Construction/BuildArrivalSystem.cs` | `CurrentWaypointIndex = 0` 제거 (160행) |

---

## Attribute 변경 (2개 시스템)

| 파일 | 변경 |
|------|------|
| `Server/Systems/Movement/PredictedMovementSystem.cs` | `[UpdateAfter(PathfindingSystem)]` → `[UpdateAfter(FlowFieldSteeringSystem)]` |
| `Server/Systems/Combat/UnifiedTargetingSystem.cs` | `[UpdateBefore(PathfindingSystem)]` → `[UpdateBefore(FlowFieldSystem)]` |

---

## 주석 갱신

| 파일 | 변경 |
|------|------|
| `Authoring/Entities/StructureAuthoring.cs` | `[Header("NavMeshObstacle용")]` → `[Header("GridObstacle 밀어내기용")]` 등 |
| `Authoring/Entities/ResourceNodeAuthoring.cs` | NavMesh 관련 주석 갱신 |
| `Shared/Components/Stats/StructureFootprint.cs` | `"NavMeshObstacle용"` → `"GridObstacle 밀어내기용"` |

---

## 체크리스트

- [ ] `GridSettingsAuthoring.cellSize` 기본값 **0.5f**
- [ ] `GridCell`: bool → **byte** 필드 2개 (IsOccupied, IsPathBlocked)
- [ ] **기존 GridUtility 메서드 bool→byte 호환 수정**: `MarkOccupied`/`UnmarkOccupied`/`IsOccupied`가 `byte` 비교(`== 0` / `== 1`)로 동작하도록 수정
- [ ] `StructureFootprint`에 `PathWidth`, `PathLength` 필드 추가
- [ ] `StructureAuthoring`에 `pathWidth`, `pathLength` 필드 추가 (기본값 = width/length)
- [ ] 구조물 프리팹 풋프린트 값 갱신 (Wall, Barracks, Turret, ResourceCenter)
- [ ] `ObstacleGridInitSystem` 수정: IsOccupied + IsPathBlocked 이중 마킹 (1회성 초기화)
- [ ] `GridOccupancyEventSystem`: **변경 없음** (IsOccupied만 담당, 기존 동작 유지)
- [ ] `MovementAuthoring` 베이킹 변경
- [ ] 신규 컴포넌트 3종 작성
- [ ] NavMesh 관련 파일 3종 삭제
- [ ] `NavMeshObstacleProxy.cs`에서 `NavMeshObstacleReference` 제거
- [ ] `MovementGoal.cs` 필드 제거 + `[MarshalAs]` 추가
- [ ] 6개 시스템 `CurrentWaypointIndex = 0` 제거
- [ ] 2개 시스템 attribute 변경
- [ ] 주석 갱신
