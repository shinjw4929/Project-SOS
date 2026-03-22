# Phase 0: Grid 유틸리티 확장

**파일**: `Assets/Scripts/Shared/Utilities/GridUtility.cs`

---

## Burst 어노테이션 규칙

모든 신규 메서드는 struct 파라미터(`int2`, `GridSettings`, `NativeArray<byte>` 등)를 받으므로 개별 `[BurstCompile]` 적용 불가 (BC1064). 기존 `GridUtility` 패턴에 따라 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`만 적용한다.

---

## GridCell 변경

`GridCell`에 경로탐색 차단 필드를 추가한다. 기존 `IsOccupied`는 건물 배치 검사에, `IsPathBlocked`는 FlowField passability 맵 생성에 사용한다.

```csharp
// Shared/Buffers/GridCell.cs
public struct GridCell : IBufferElementData
{
    public byte IsOccupied;      // 건물 배치 점유 (0=비점유, 1=점유)
    public byte IsPathBlocked;   // 경로탐색 차단 (0=통과 가능, 1=차단)
}
```

**bool 대신 byte 사용**: GridSettingsAuthoring에서 `Reinterpret<byte>().AsNativeArray()` + `MemSet(0)`으로 초기화한다. `bool`은 컴파일러 레이아웃(패딩)에 따라 `sizeof(GridCell)`이 예측 불가하므로 `byte`를 사용하여 크기를 정확히 2 byte로 고정한다.

**배치 vs 경로탐색 분리 이유**: 벽은 배치 풋프린트(4×4)가 경로탐색 풋프린트(2×2 중앙)보다 크다. 건물 겹침 방지에는 전체 4×4를, 경로탐색에는 중앙 2×2만 사용해야 하므로 두 필드가 필요하다.

**책임 분리**:
- `IsOccupied`: `GridOccupancyEventSystem` (LateSimulation)이 마킹/해제
- `IsPathBlocked`: `GridObstacleResponseSystem` (마킹) / `GridObstacleCleanupSystem` (해제)
- 각 필드를 하나의 시스템만 관리하여 이중 마킹/해제 경합 방지

---

## 추가 메서드

### CellCenterToWorld
```csharp
// 단일 셀 중심의 월드 좌표 반환 (XZ 평면, Y=0)
// FlowFieldSteeringSystem에서 다음 셀 목표 좌표 계산에 사용
// 사용처: FlowFieldSteeringSystem (Phase 2.5)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static float3 CellCenterToWorld(int2 cell, GridSettings settings)
{
    // GridOrigin은 float2 → float3 변환 필요 (기존 GridToWorld 패턴)
    float2 center = settings.GridOrigin + (new float2(cell) + 0.5f) * settings.CellSize;
    return new float3(center.x, 0, center.y);
}
```

### IsPassable
```csharp
// 범위 체크 + 경로탐색 차단 체크
// 경계 밖은 blocked(통과 불가)로 처리: x < 0 || y < 0 || x >= gridSizeX || y >= gridSizeY
// IsPathBlocked를 기준으로 판정 (IsOccupied 아님)
// 사용처: IsPassableForSize 내부 헬퍼, FlowFieldCore.ComputeField BFS 내부에서는 passabilityMap 직접 인덱싱
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsPassable(NativeArray<byte> map, int x, int y, int gridSizeX, int gridSizeY)
{
    if (x < 0 || y < 0 || x >= gridSizeX || y >= gridSizeY)
        return false;
    return map[y * gridSizeX + x] == 0;  // 0 = passable, 1 = blocked
}
```

### IsPassableForSize
```csharp
// 유닛 크기 고려 — 주변 셀 확장 체크
// cellPadding=0 → Small (자기 셀만), cellPadding=1 → Large (주변 1칸 포함)
// cellPadding=0일 때 IsPassable과 동일한 동작
// 경계 밖으로 확장되는 경우 blocked로 처리 (대형 유닛의 맵 가장자리 진입 차단)
// 사용처: BuildPassabilityMap 내부
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsPassableForSize(NativeArray<byte> map, int x, int y, int gridSizeX, int gridSizeY, int cellPadding)
```

### BuildPassabilityMap
```csharp
// GridCell 버퍼의 IsPathBlocked → 유닛 크기별 passability 맵 생성
// 0 = passable, 1 = blocked
// IsPathBlocked 기반 (IsOccupied가 아님) — 벽의 경로탐색 점유는 배치보다 작음
// 전제 조건: output는 호출자가 gridSize.x * gridSize.y 크기로 할당해야 함
// 사용처: FlowFieldSystem Phase 0 (IsGridStale 시에만 호출)
// [MethodImpl] 미적용: 루프 포함 메서드이므로 인라이닝 제외 (기존 GridUtility 패턴 동일)
public static void BuildPassabilityMap(DynamicBuffer<GridCell> cells, int2 gridSize, int cellPadding, NativeArray<byte> output)
```

**cellPadding 적용 방식**: `BuildPassabilityMap`은 내부에서 `IsPassableForSize(rawMap, x, y, gridSizeX, gridSizeY, cellPadding)`을 셀마다 호출하여 유닛 크기에 따른 확장 차단을 반영한다.
- `cellPadding=0` (Small): 각 셀의 `IsPathBlocked` 값만 반영 (확장 없음)
- `cellPadding=1` (Large): `IsPathBlocked=1`인 셀 주변 1칸도 blocked로 마킹 (대형 유닛 통과 차단)

### MarkPathBlocked / UnmarkPathBlocked
```csharp
// 경로탐색 풋프린트 마킹 — 배치 풋프린트의 중앙 부분집합
// gridPos: 건물 배치 좌하단 그리드 좌표
// width/length: 배치 풋프린트 크기 (StructureFootprint.Width/Length)
// pathWidth/pathLength: 경로탐색 풋프린트 크기 (StructureFootprint.PathWidth/PathLength)
// 중앙 오프셋 자동 계산: offsetX = (width - pathWidth) / 2
//
// 사용처: ObstacleGridInitSystem (1회성 초기화), GridObstacleResponseSystem (건설 시)
public static void MarkPathBlocked(DynamicBuffer<GridCell> cells, int gridX, int gridY,
    int width, int length, int pathWidth, int pathLength, int gridSizeX)

public static void UnmarkPathBlocked(DynamicBuffer<GridCell> cells, int gridX, int gridY,
    int width, int length, int pathWidth, int pathLength, int gridSizeX)
```

**중앙 오프셋 계산 예시**:
```
Wall: width=4, pathWidth=2 → offsetX = (4-2)/2 = 1
      length=4, pathLength=2 → offsetY = (4-2)/2 = 1
      배치 (0,0)-(3,3), 경로탐색 (1,1)-(2,2)

Barracks: width=6, pathWidth=6 → offsetX = 0
          length=6, pathLength=6 → offsetY = 0
          배치 = 경로탐색 (동일)
```

**구현 참고**: `offsetX = (width - pathWidth) / 2`, `offsetY = (length - pathLength) / 2`를 모두 계산하여 X/Y 양축에 적용한다.

**홀수 차이 방지**: `(width - pathWidth)`가 홀수이면 정수 나눗셈 내림으로 경로 영역이 비대칭이 된다. `Assert((width - pathWidth) % 2 == 0)`로 검증한다. 현재 모든 구조물은 짝수 차이이므로 문제없으나, 향후 추가 시 주의 필요.

### ResourceNodeExclusionDistance 갱신
```csharp
// 기존: 9 (1.0m 셀 × 9 = 9m)
// 변경: 18 (0.5m 셀 × 18 = 9m, 동일한 물리 거리 유지)
public const int ResourceNodeExclusionDistance = 18;
```

---

## 체크리스트

- [ ] Phase 4-A에서 추가된 `GridCell.IsPathBlocked` 필드 활용 확인
- [ ] `MarkPathBlocked` / `UnmarkPathBlocked` 구현 (중앙 오프셋 자동 계산)
- [ ] `CellCenterToWorld` 구현 (float2→float3 변환, Y=0)
- [ ] `IsPassable` 구현 (경계 밖 = blocked, `IsPathBlocked` 기반)
- [ ] `IsPassableForSize` 구현 (cellPadding 파라미터, 경계 밖 = blocked)
- [ ] `BuildPassabilityMap` 구현 (`IsPathBlocked` 기반)
- [ ] `ResourceNodeExclusionDistance` 9 → 18
- [ ] 루프 없는 메서드에 `[MethodImpl(MethodImplOptions.AggressiveInlining)]` 적용 (BuildPassabilityMap, MarkPathBlocked, UnmarkPathBlocked 제외)
- [ ] 모든 메서드에 `public` 접근 제한자 (다른 어셈블리 Server에서 호출)
- [ ] 기존 GridUtility 메서드와 네이밍/파라미터 일관성 확인
