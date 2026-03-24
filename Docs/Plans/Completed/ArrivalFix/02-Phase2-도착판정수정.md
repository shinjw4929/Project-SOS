# Phase 2: 도착 판정 수정 (Gathering + Build)

## 목표

P0(실패 확인) 및 P1(잠재적 실패) 문제 해결 + BuildArrivalSystem CellSize 마진 통일.

---

## 2-A. WorkerGatheringSystem — GetArrivalDistance에 CellSize 마진 추가 (P0)

### OnCreate에 GridSettings RequireForUpdate 추가

**파일**: `WorkerGatheringSystem.cs:34-47`

```csharp
// 기존 (line 36):
state.RequireForUpdate<NetworkStreamInGame>();

// 변경:
state.RequireForUpdate<NetworkStreamInGame>();
state.RequireForUpdate<GridSettings>();
```

### OnUpdate에서 CellSize 읽기

**파일**: `WorkerGatheringSystem.cs:50-88`

```csharp
// line 58 이후 (deltaTime 선언 후)에 추가:
float cellSize = SystemAPI.GetSingleton<GridSettings>().CellSize;
```

`cellSize`를 `ProcessMovingToGather`, `ProcessMovingToReturn`, `ProcessWaitingForNode` 호출에 전달해야 한다. 3가지 방법 중 가장 간결한 방법 선택:

**방법**: `GetArrivalDistance`에 cellSize 파라미터 추가.

### GetArrivalDistance 수정

**파일**: `WorkerGatheringSystem.cs:548-552`

```csharp
// 기존:
private float GetArrivalDistance(Entity targetEntity, Entity unitEntity)
{
    return ArrivalUtility.GetInteractionArrivalDistance(
        targetEntity, unitEntity, in _obstacleRadiusLookup, in _workRangeLookup);
}

// 변경:
private float GetArrivalDistance(Entity targetEntity, Entity unitEntity, float cellSize)
{
    return ArrivalUtility.GetGridCompensatedArrivalDistance(
        targetEntity, unitEntity, in _obstacleRadiusLookup, in _workRangeLookup, cellSize);
}
```

### 호출부 수정

`GetArrivalDistance` 호출에 `cellSize` 인자 추가:

| 위치 | 기존 | 변경 |
|------|------|------|
| `ProcessMovingToGather:157` | `GetArrivalDistance(nodeEntity, entity)` | `GetArrivalDistance(nodeEntity, entity, cellSize)` |
| `ProcessMovingToReturn:302` | `GetArrivalDistance(returnPoint, entity)` | `GetArrivalDistance(returnPoint, entity, cellSize)` |
| `ProcessWaitingForNode:470` | `GetArrivalDistance(nodeEntity, entity)` | `GetArrivalDistance(nodeEntity, entity, cellSize)` |

`cellSize`를 각 메서드에 전달하는 방법: OnUpdate에서 읽은 `cellSize`를 필드에 저장.

```csharp
// 클래스 필드 추가 (line 32 부근):
private float _cellSize;

// OnUpdate에서 (line 58 이후):
_cellSize = SystemAPI.GetSingleton<GridSettings>().CellSize;

// 각 Process 메서드 내부에서:
GetArrivalDistance(nodeEntity, entity, _cellSize)
```

---

## 2-B. WorkerGatheringSystem — 3D → XZ 거리 변경 (P0/P1)

이동 시스템(PredictedMovementSystem, MovementArrivalSystem)은 XZ 거리를 사용한다. 채집 시스템도 통일.

### ProcessMovingToGather (line 156)

```csharp
// 기존:
float distance = math.distance(workerPos, nodePos);

// 변경:
float distance = math.distance(workerPos.xz, nodePos.xz);
```

### ProcessMovingToReturn (line 301)

```csharp
// 기존:
float distance = math.distance(workerPos, centerPos);

// 변경:
float distance = math.distance(workerPos.xz, centerPos.xz);
```

### ProcessWaitingForNode (line 468)

```csharp
// 기존:
float distance = math.distance(workerPos, nodePos);

// 변경:
float distance = math.distance(workerPos.xz, nodePos.xz);
```

### FindNearestResourceCenter (line 585)

```csharp
// 기존:
float dist = math.distance(workerPos, transform.ValueRO.Position);

// 변경:
float dist = math.distance(workerPos.xz, transform.ValueRO.Position.xz);
```

---

## 2-C. WorkerGatheringSystem — ArrivalRadius 누락 보완 (P1)

`TransitionToReturn`과 `DecideNextAction`에서 접근점을 설정하면서 ArrivalRadius를 설정하지 않는 문제.

### TransitionToReturn (line 508-543)

MovementWaypoints를 직접 접근할 수 없다 (Query에 포함되어 있지 않음). Lookup 추가 필요.

**Lookup 추가** (line 23-31):

```csharp
// 기존 Lookup 목록 끝에 추가:
private ComponentLookup<MovementWaypoints> _movementWaypointsLookup;
```

**OnCreate에 등록** (line 34-47):

```csharp
_movementWaypointsLookup = state.GetComponentLookup<MovementWaypoints>(false);
```

**UpdateLookups에 추가** (line 90-102):

```csharp
_movementWaypointsLookup.Update(ref state);
```

**TransitionToReturn 내부** (line 536-537, `movementGoal.ValueRW.IsPathDirty = true;` 아래):

```csharp
// ArrivalRadius 설정 (Dead Zone 방지)
if (_movementWaypointsLookup.HasComponent(entity))
{
    float workRange = _workRangeLookup.TryGetComponent(entity, out var wr)
        ? wr.Value : 1.0f;
    _movementWaypointsLookup.GetRefRW(entity).ValueRW.ArrivalRadius =
        ArrivalUtility.GetSafeArrivalRadius(workRange);
}
```

### DecideNextAction (line 375-438)

`movementGoal.ValueRW.IsPathDirty = true;` 가 두 군데 (line 427, 436) 있다. 양쪽 모두 아래에 추가:

```csharp
// ArrivalRadius 설정 (Dead Zone 방지)
if (_movementWaypointsLookup.HasComponent(workerEntity))
{
    float workRange = _workRangeLookup.TryGetComponent(workerEntity, out var wr)
        ? wr.Value : 1.0f;
    _movementWaypointsLookup.GetRefRW(workerEntity).ValueRW.ArrivalRadius =
        ArrivalUtility.GetSafeArrivalRadius(workRange);
}
```

---

## 2-D. WorkerGatheringSystem — 디버그 로그 제거

### ProcessMovingToGather (line 159-169)

```csharp
// 삭제 대상:
// [DEBUG] 채집 도착 판정 로그
{
    FixedString128Bytes msg = "GatherArrival";
    GameLogger.Field(ref msg, "idx", entity.Index);
    GameLogger.Field(ref msg, "dist", (int)(distance * 100));
    GameLogger.Field(ref msg, "need", (int)(arrivalDist * 100));
    GameLogger.Field(ref msg, "dy", (int)((workerPos.y - nodePos.y) * 100));
    GameLogger.Pos(ref msg, "w", workerPos);
    GameLogger.Pos(ref msg, "n", nodePos);
    GameLogger.Warning(LogWorld.Server, LogCategory.Economy, in msg);
}
```

### ProcessMovingToReturn (line 304-314)

```csharp
// 삭제 대상:
// [DEBUG] 반납 도착 판정 로그
{
    FixedString128Bytes msg = "ReturnArrival";
    GameLogger.Field(ref msg, "idx", entity.Index);
    GameLogger.Field(ref msg, "dist", (int)(distance * 100));
    GameLogger.Field(ref msg, "need", (int)(returnArrivalDist * 100));
    GameLogger.Field(ref msg, "dy", (int)((workerPos.y - centerPos.y) * 100));
    GameLogger.Pos(ref msg, "w", workerPos);
    GameLogger.Pos(ref msg, "c", centerPos);
    GameLogger.Warning(LogWorld.Server, LogCategory.Economy, in msg);
}
```

---

## 2-E. BuildArrivalSystem — CellSize 마진 통일

**파일**: `BuildArrivalSystem.cs:115-116`

```csharp
// 기존:
float arrivalDist = ArrivalUtility.GetInteractionArrivalDistance(
    pending.StructureRadius, workRange) + gridSettings.CellSize * 0.7f;

// 변경:
float arrivalDist = ArrivalUtility.GetGridCompensatedArrivalDistance(
    pending.StructureRadius, workRange, gridSettings.CellSize);
```

재시도 로직(line 138-189)은 안전망으로 유지. 마진 증가(1.05m → 1.35m)로 사실상 미발생.

---

## 변경 파일 요약

| 파일 | 변경 내용 |
|------|----------|
| `WorkerGatheringSystem.cs` | Lookup 추가(`MovementWaypoints`), 필드 추가(`_cellSize`), OnCreate(`RequireForUpdate<GridSettings>`), OnUpdate(`cellSize` 읽기), `GetArrivalDistance`(`cellSize` 파라미터 + `GetGridCompensatedArrivalDistance`), 3D→XZ 4곳, ArrivalRadius 설정 3곳(TransitionToReturn 1 + DecideNextAction 2), DEBUG 로그 2블록 삭제 |
| `BuildArrivalSystem.cs` | line 115-116: `GetGridCompensatedArrivalDistance` 사용 |
