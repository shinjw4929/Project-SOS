# Phase 3: NavMesh 장애물 시스템 대체

---

## 책임 분리 원칙

각 GridCell 필드를 하나의 시스템만 관리하여 이중 마킹/해제 경합을 방지한다.

| 필드 | 마킹 | 해제 |
|------|------|------|
| `IsOccupied` | `GridOccupancyEventSystem` (LateSimulation) | `GridOccupancyEventSystem` (LateSimulation) |
| `IsPathBlocked` | **`GridObstacleResponseSystem`** (Simulation) | **`GridObstacleCleanupSystem`** (Simulation) |

예외: `ObstacleGridInitSystem`(Initialization)은 초기화 시 IsOccupied + IsPathBlocked 모두 마킹 (1회성).

---

## GridObstacleResponseSystem (NavMeshObstacleSpawnSystem 대체)

**신규 파일**: `Assets/Scripts/Server/Systems/Movement/GridObstacleResponseSystem.cs`

### 시스템 배치

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateBefore(typeof(FlowFieldSystem))]
```

### 트리거

`NeedsNavMeshObstacle` 태그 감지 (기존 태그 재사용)

### 기능

1. 건물 footprint 내부 엔티티 밀어내기
2. 주변 8m 유닛 `IsPathDirty=true`
3. **`IsPathBlocked` 마킹만** (경로탐색 풋프린트, PathWidth × PathLength 중앙)
   - `IsOccupied`는 마킹하지 않음 — `GridOccupancyEventSystem`이 담당
4. **Flow Field 캐시 전체 무효화** 트리거 (`FlowFieldCacheData.IsGridStale=true`)
5. `GridObstacleCleanup` 부착 (건물 파괴 감지용)
6. **`NeedsNavMeshObstacle` 비활성화** (`SetComponentEnabled false`)

### 구현 전략: 2패스 방식

ISystem `[BurstCompile]`에서 중첩 `SystemAPI.Query`는 소스 제너레이터 제약으로 안전하지 않을 수 있다. 따라서 2패스로 분리:

1. **1패스** (메인 스레드): NeedsNavMeshObstacle 엔티티를 NativeList에 수집 (위치, 풋프린트 정보) + IsPathBlocked 마킹 + GridObstacleCleanup 부착
2. **2패스**: 수집된 건물 목록 기반으로 주변 유닛 순회 → 밀어내기 + IsPathDirty 설정

**병렬화 대안**: 2패스의 유닛 순회는 IJobEntity + `[ReadOnly] NativeArray<BuildingInfo>` 패턴으로 병렬화 가능. 이 경우 1패스에서 `Allocator.TempJob`으로 할당하여 Job에서 참조 가능하게 한다. 건물 건설이 드문 이벤트이므로 초기 구현은 메인 스레드 2중 foreach로 시작하고, 프로파일링 결과에 따라 병렬화를 검토한다.

### ECB 타이밍

`Allocator.Temp` + 즉시 `Playback`으로 같은 프레임에 반영. 병렬화 시 `Allocator.TempJob`으로 변경 필요.

### Burst 호환

ISystem + `[BurstCompile]` 가능 (managed 객체 제거됨)

**필수**: `MovementGoal.IsPathDirty`와 `IsPathPartial`에 `[MarshalAs(UnmanagedType.U1)]` 추가 (Burst blittable 필수).

### passability 타이밍

GridObstacleResponseSystem이 GridCell.IsPathBlocked를 **직접 수정**하므로, 같은 프레임 후속 FlowFieldSystem에서 passability 맵을 재빌드하면 최신 상태가 반영된다. **프레임 스킵 불필요**.

- GridObstacleResponseSystem (Sim): IsPathBlocked 마킹 + IsGridStale=true
- FlowFieldSystem (Sim, UpdateAfter): IsGridStale 감지 → passability 맵 재빌드 (최신 GridCell 반영) → 같은 프레임에서 BFS 재계산

---

## GridObstacleCleanup (신규 컴포넌트)

```csharp
// Shared/Components/Data/GridObstacleCleanup.cs
public struct GridObstacleCleanup : ICleanupComponentData
{
    public int2 GridPosition;
    public int Width;       // 배치 풋프린트 (월드 좌표 복원용)
    public int Length;
    public int PathWidth;   // 경로탐색 풋프린트 (UnmarkPathBlocked용)
    public int PathLength;
}
```

- **GridObstacleResponseSystem에서만 부착** (NeedsNavMeshObstacle 처리 시)
- GridObstacleCleanupSystem이 독점 소유/제거
- unmanaged struct → `[BurstCompile]` 가능

---

## GridObstacleCleanupSystem (NavMeshObstacleCleanupSystem 대체)

**신규 파일**: `Assets/Scripts/Server/Systems/Movement/GridObstacleCleanupSystem.cs`

### 시스템 배치

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(ServerDeathSystem))]
[UpdateBefore(typeof(FlowFieldSystem))]  // UnmarkPathBlocked + IsGridStale=true가 같은 프레임에서 FlowFieldSystem에 반영되도록 보장
```

### 쿼리

`GridObstacleCleanup` + `WithNone<StructureTag, ResourceNodeTag>` (파괴된 건물만 매칭)

### 동작

1. `GridObstacleCleanup`에서 GridPosition, Width, Length, PathWidth, PathLength 읽기
2. **`IsPathBlocked` 해제만**: `GridUtility.UnmarkPathBlocked` (경로탐색 풋프린트)
   - `IsOccupied` 해제는 하지 않음 — `GridOccupancyEventSystem`이 담당
3. **월드 좌표 복원**: `GridUtility.GridToWorld(cleanup.GridPosition.x, cleanup.GridPosition.y, cleanup.Width, cleanup.Length, gridSettings)` → 밀어내기/Dormant 깨우기 반경 계산에 사용
4. 주변 Partial Path 무효화
5. Dormant 적 깨우기 (`EnemyState.CurrentState == Dormant → Idle`)
6. **Flow Field 캐시 전체 무효화** 트리거
7. `GridObstacleCleanup` 컴포넌트 제거 (ECB)

---

## 체크리스트

- [ ] `GridObstacleResponseSystem` 구현 (NeedsNavMeshObstacle 트리거, 2패스 방식)
- [ ] 시스템 attribute: `UpdateInGroup`, `WorldSystemFilter`, `UpdateBefore(FlowFieldSystem)`
- [ ] footprint 밀어내기 로직 이전 (NativeList 수집 → 유닛 순회)
- [ ] 주변 유닛 `IsPathDirty=true` 설정
- [ ] **`IsPathBlocked`만 마킹** (`MarkPathBlocked`, IsOccupied는 GridOccupancyEventSystem 담당)
- [ ] 캐시 무효화 트리거
- [ ] `NeedsNavMeshObstacle` 비활성화
- [ ] `GridObstacleCleanup` ICleanupComponentData 정의 (PathWidth/PathLength 포함)
- [ ] `GridObstacleCleanupSystem` 구현 (attribute: `UpdateAfter(ServerDeathSystem)`, `UpdateBefore(FlowFieldSystem)`)
- [ ] **`IsPathBlocked`만 해제** (`UnmarkPathBlocked`, IsOccupied 해제는 GridOccupancyEventSystem 담당)
- [ ] 월드 좌표 복원: `GridToWorld(gridPos, width, length, settings)` 사용
- [ ] Partial Path 무효화 + Dormant 적 깨우기
- [ ] `MovementGoal.IsPathDirty`, `IsPathPartial`에 `[MarshalAs(UnmanagedType.U1)]` 추가
