# Phase 2.5: FlowFieldSteeringSystem (PathFollowSystem 대체)

**파일**: `Assets/Scripts/Server/Systems/Movement/PathFollowSystem.cs` → 재작성

---

## 시스템 배치

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(FlowFieldSystem))]
[UpdateBefore(typeof(PredictedMovementSystem))]
```

---

## Burst / Job 병렬화

- 시스템 전체 `[BurstCompile]` 적용 가능 (모든 의존성이 unmanaged)
- IJobEntity 병렬화 가능:
  - ReadOnly: `GridSettings`
  - ReadWrite: `MovementWaypoints` (엔티티당 독립), `MovementGoal` (캐시 미스 시 IsPathDirty 설정)

### NativeContainer Job 전달 패턴

`FlowFieldCacheData` 싱글톤 내부의 NativeContainer는 **OnUpdate에서 꺼내 Job struct 필드로 전달**한다. 싱글톤을 Job 내부에서 직접 읽지 않음.

```csharp
var cacheData = SystemAPI.GetSingleton<FlowFieldCacheData>();

var job = new FlowFieldSteeringJob
{
    SmallFieldPool = cacheData.SmallFieldPool,         // [ReadOnly]
    LargeFieldPool = cacheData.LargeFieldPool,         // [ReadOnly]
    SmallKeyToPoolIndex = cacheData.SmallKeyToPoolIndex, // [ReadOnly]
    LargeKeyToPoolIndex = cacheData.LargeKeyToPoolIndex, // [ReadOnly]
    GridCellCount = cacheData.GridCellCount,
    // ... GridSettings, MovementWaypoints 등
};
```

---

## 쿼리 필터

```csharp
// MovementWaypoints enabled 유닛 순회
// FlyingTag 제외: Phase 2에서 FlyingTag 유닛은 직선 이동으로 처리 (FlowFieldRef 미할당)
// FlyingTag 유닛이 쿼리에 포함되면 FlowFieldRef.Key 미설정 → 캐시 미스 → IsPathDirty=true 무한 루프 발생
.WithAny<UnitTag, EnemyTag>()  // 기존 PathFollowSystem 패턴 유지
.WithNone<FlyingTag>()
```

쿼리에 `GridPathfindingSize`(RefRO)를 포함하여 유닛의 CellPadding으로 Small/Large 캐시를 선택한다.

---

## 싱글톤 의존성

```csharp
state.RequireForUpdate<GridSettings>();
state.RequireForUpdate<FlowFieldCacheData>();
```

---

## 매 프레임 동작

```
이동 중 유닛(MovementWaypoints enabled, WithNone<FlyingTag>) 순회:

1. 유닛 현재 위치 → 그리드 셀 변환 (GridUtility.WorldToGrid)
2. GridPathfindingSize.CellPadding → Small/Large 캐시 선택
3. FlowFieldRef.Key로 해당 캐시에서 Flow Field 조회
4. 현재 셀의 방향(byte) → 다음 셀 좌표(currentCell + dirOffset) 계산
5. **목적지 셀 판정**: 좌표 비교 방식 사용
   - `int2 destCell = GridUtility.WorldToGrid(goal.Destination, gridSettings)`
   - `currentCell == destCell`이면 목적지 셀 (방향=255 판정과 혼동 방지 — 255는 도달 불가 셀에도 사용됨)
6. 분기:
   ├─ [목적지 셀] (currentCell == destCell):
   │     Current = MovementGoal.Destination (실제 월드 좌표)
   │     HasNext = false → MovementArrivalSystem이 도착 판정
   ├─ [방향=None(255), 목적지 셀 아님]:
   │     IsPathDirty = true → 다음 프레임 FlowFieldSystem에서 Partial Path 재계산
   │     (장애물 동적 생성으로 현재 셀이 도달 불가가 된 경우)
   └─ [중간 셀]:
         Current = 다음 셀 중심 월드 좌표 (GridUtility.CellCenterToWorld)
         2단계 look-ahead:
           ├─ look-ahead 셀이 목적지(== destCell) → Next = Destination, HasNext = true
           ├─ look-ahead 셀 방향이 None(255) → HasNext = false (look-ahead 중단)
           ├─ **look-ahead 캐시 미스** (FlowFieldRef.Key로 조회 실패) → HasNext = false, IsPathDirty = true
           └─ 그 외 → Next = look-ahead 셀 중심, HasNext = true
```

**전제 조건**: FlowFieldSystem이 이미 FlowFieldRef를 할당한 유닛만 처리. FlowFieldRef.Key == -1인 경우 스킵.

> **참고**: CellCenterToWorld는 GridSettings.CellSize(0.5m)를 사용하므로 셀 크기 변경에 자동 대응.

---

## 핵심: HasNext 설정

MovementArrivalSystem은 `HasNext=true`이면 도착 판정을 스킵한다 (`CheckArrival` 첫 줄).

- **중간 셀에서 `HasNext=false` 설정 금지**: ArrivalRadius(MovementAuthoring에서 설정, 기본값 0.5f) 이내 진입 시 매 셀마다 도착 오판 발생
- **반드시 중간 셀에서는 `HasNext=true` 유지**

---

## PredictedMovementSystem HasNext 소비와의 상호작용

PredictedMovementSystem은 `HasNext=true`이고 `distSq < 0.25f`일 때 `Current = Next; HasNext = false`로 당겨쓴다.
FlowFieldSteeringSystem은 매 프레임 유닛의 **현재 위치**를 기반으로 독립적으로 값을 계산하므로, PredictedMovementSystem의 상태와 무관하게 항상 올바른 방향을 주입한다. 실행 순서(FlowFieldSteeringSystem → PredictedMovementSystem)에 의해 같은 프레임 내에서 먼저 최신 값을 주입한 후 PredictedMovementSystem이 소비한다.

---

## CurrentWaypointIndex 전환기 처리

Phase 4 이전에 단독 구현 시, `MovementGoal.CurrentWaypointIndex`는 여전히 존재하지만 FlowFieldSteeringSystem은 이 필드를 **무시**한다. 0으로 유지되며 어떤 로직에도 참조하지 않는다.

---

## 캐시 미스 처리

FlowFieldRef.Key로 캐시 조회 실패 시 (전체 캐시 무효화 후 발생):

1. `IsPathDirty=true` 설정 → 다음 프레임 FlowFieldSystem에서 재계산 (lazy re-pathing)
2. 현재 프레임은 기존 `MovementWaypoints.Current` 유지 (유닛 정지 또는 이전 방향 유지)
3. 1프레임 지연이지만 벽(Wall) 물리 충돌이 안전망 역할 (건물 콜라이더도 충돌 레이어에 포함되어야 함)

---

## PredictedMovementSystem 영향

**변경 없음**. 기존대로 `MovementWaypoints.Current`를 향해 이동.

---

## 체크리스트

- [ ] 쿼리에 `WithNone<FlyingTag>` 필터 추가
- [ ] `FlowFieldRef.Key == -1` 스킵 로직
- [ ] Flow Field 방향 조회 → 다음 셀 좌표 변환
- [ ] 목적지 셀 판정: **좌표 비교** (`currentCell == destCell`), `Current = Destination`, `HasNext = false`
- [ ] 중간 셀: `Current = 셀 중심`, `HasNext = true`, `Next = 2단계 look-ahead` (look-ahead 캐시 미스 시 `HasNext=false` + `IsPathDirty=true`)
- [ ] 캐시 미스 시 `IsPathDirty=true` (lazy re-pathing)
- [ ] 시스템 attribute: `UpdateInGroup`, `WorldSystemFilter`, `UpdateAfter`, `UpdateBefore`
- [ ] `RequireForUpdate<GridSettings>()`, `RequireForUpdate<FlowFieldCacheData>()`
- [ ] `[BurstCompile]` 적용
