# Phase 3: NavMesh 장애물 시스템 대체

---

## GridObstacleResponseSystem (NavMeshObstacleSpawnSystem 대체)

**신규 파일**: `Assets/Scripts/Server/Systems/Movement/GridObstacleResponseSystem.cs`

### 시스템 배치

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateBefore(typeof(FlowFieldSystem))]  // FlowFieldSystem이 [UpdateAfter(GridObstacleResponseSystem)]
```

### 트리거

`NeedsNavMeshObstacle` 태그 감지 (기존 태그 재사용)

### 기능

1. 건물 footprint 내부 엔티티 밀어내기 (기존 `PushAndInvalidateNearbyPaths` 로직 이전)
2. 주변 8m 유닛 `IsPathDirty=true`
3. **Flow Field 캐시 전체 무효화** 트리거 (`FlowFieldCacheData.IsGridStale=true`)
4. `GridObstacleCleanup` 부착 (건물 파괴 감지용 cleanup component)
5. **`NeedsNavMeshObstacle` 비활성화** (`ecb.SetComponentEnabled<NeedsNavMeshObstacle>(entity, false)`)

### ECB 타이밍

기존 `NavMeshObstacleSpawnSystem` 패턴에 따라 `Allocator.Temp` + 즉시 `Playback`으로 같은 프레임에 반영.

### 제거 대상

- GameObject 생성 로직
- `NavMeshObstacleReference` managed component 참조

### Burst 호환

ISystem + `[BurstCompile]` 가능 (managed 객체 제거됨)

**필수**: `MovementGoal.IsPathDirty`와 `IsPathPartial`은 `bool` 필드이다. `GridObstacleResponseSystem`이 ISystem `[BurstCompile]`로 전환되면 `RefRW<MovementGoal>`을 통해 bool 필드를 수정하므로, `MovementGoal`의 두 bool 필드에 `[MarshalAs(UnmanagedType.U1)]`를 **반드시 추가**해야 한다 (CLAUDE.md Burst 제약사항).

### passability 타이밍 주의

캐시 무효화와 IsPathDirty가 동일 프레임에 발생하면, FlowFieldSystem이 stale 그리드(GridOccupancyEventSystem 미갱신)로 재계산할 수 있음.

**해결**: FlowFieldSystem Collect Phase에서 `IsGridStale=true`이면 dirty 유닛 계산을 스킵하고, 다음 프레임(그리드 갱신 후)에 재계산.

**캐시 무효화 중 이동 유닛 fallback**: 캐시가 무효화된 프레임에서 FlowFieldSteeringSystem이 방향 조회 실패 시, 기존 `MovementWaypoints.Current`를 유지한다 (유닛 정지 또는 이전 방향 관성 이동). 다음 프레임에 FlowFieldSystem이 재계산 후 정상 복구.

---

## GridObstacleCleanup (신규 컴포넌트)

> **네이밍**: 기존 `GridOccupancyCleanup` 패턴과 일관성 유지. "Needs" 접두사는 처리 요청 태그(`NeedsNavMeshObstacle`)에만 사용.

```csharp
// Shared/Components/Data/GridObstacleCleanup.cs
public struct GridObstacleCleanup : ICleanupComponentData
{
    public int2 GridPosition;
    public int Width;
    public int Length;
}
```

### 타이밍 문제와 해결

**문제**: `GridOccupancyCleanup`을 직접 쿼리하면 감지 불가
- ServerDeathSystem(EndSim ECB) → 엔티티 파괴
- 같은 프레임 LateSim에서 GridOccupancyEventSystem이 `GridOccupancyCleanup`을 BeginSim ECB로 제거 예약
- 다음 프레임 Sim 시작 전에 이미 사라짐

**해결**: 별도 cleanup component 독점 소유
- **GridObstacleResponseSystem에서만 부착** (NeedsNavMeshObstacle 처리 시)
- BuildingUtility/ObstacleGridInitSystem에서는 부착하지 않음 (ECB 이중 AddComponent 방지)
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
```

### 싱글톤 의존성

```csharp
state.RequireForUpdate<GridSettings>();       // GridToWorld 호출에 필요
state.RequireForUpdate<FlowFieldCacheData>(); // 캐시 무효화 트리거
```

### 쿼리

`GridObstacleCleanup` + `WithNone<StructureTag, ResourceNodeTag>` (파괴된 건물만 매칭)

### 동작

1. `GridObstacleCleanup.GridPosition` + `Width`, `Length` → `GridUtility.GridToWorld(gridPos.x, gridPos.y, width, length, settings)` → 월드 좌표 변환
2. 건물 파괴 시 주변 Partial Path 무효화
3. Dormant 적 깨우기 (`EnemyState.CurrentState == Dormant → Idle` 변경)
4. **Flow Field 캐시 전체 무효화** 트리거
5. `GridObstacleCleanup` 컴포넌트 제거 (ECB)

### ECB 타이밍

`Allocator.Temp` + 즉시 `Playback` (기존 패턴). 같은 프레임에 cleanup component 제거되어 엔티티 완전 소멸은 `GridOccupancyCleanup` 제거 시점에 의존.

### 제거 대상

- `NavMeshObstacleReference` 참조
- `GameObject.Destroy` 호출

---

## 체크리스트

- [ ] `GridObstacleResponseSystem` 구현 (NeedsNavMeshObstacle 트리거)
- [ ] 시스템 attribute: `UpdateInGroup`, `WorldSystemFilter`, `UpdateBefore(FlowFieldSystem)`
- [ ] footprint 밀어내기 로직 이전
- [ ] 주변 유닛 `IsPathDirty=true` 설정
- [ ] 캐시 무효화 트리거
- [ ] 처리 완료 후 `NeedsNavMeshObstacle` 비활성화 (`SetComponentEnabled false`)
- [ ] `GridObstacleCleanup` ICleanupComponentData 정의
- [ ] `GridObstacleCleanupSystem` 구현 (attribute: `UpdateInGroup`, `WorldSystemFilter`, `UpdateAfter(ServerDeathSystem)`)
- [ ] 파괴 시 Partial Path 무효화 + Dormant 적 깨우기
- [ ] passability 타이밍 1프레임 스킵 검증
- [ ] `MovementGoal.IsPathDirty`, `IsPathPartial`에 `[MarshalAs(UnmanagedType.U1)]` **추가** (ISystem Burst 필수)
- [ ] `GridObstacleCleanupSystem`에 `RequireForUpdate<GridSettings>()` 설정
