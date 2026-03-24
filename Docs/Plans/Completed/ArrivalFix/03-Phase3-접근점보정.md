# Phase 3: 접근점 계산 보정 (요청 시스템)

## 목표

effectiveRadius 기반 접근점 안전장치 적용 (미래 대비).

현재 모든 건물에서 effectiveRadius = ObstacleRadius이므로 접근점 위치 변화 없음. 향후 ObstacleRadius < gridBlockedHalfExtent인 건물 추가 시 접근점이 blocked 셀 안에 배치되는 것을 방지.

> **Fallback**: 타겟에 StructureFootprint가 없으면 ObstacleRadius 그대로 사용 (기존 동작 유지).

---

## 3-A. HandleGatherRequestSystem

**파일**: `HandleGatherRequestSystem.cs`

### Lookup 추가

```csharp
// line 28 이후 (기존 ReadOnly Lookup 목록에 추가):
[ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
```

### OnCreate에 등록

```csharp
// line 52 이후:
_footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
```

### OnUpdate에 Update 추가

```csharp
// line 73 이후:
_footprintLookup.Update(ref state);
```

### GridSettings 싱글톤 참조

**OnCreate에 RequireForUpdate 추가** (다른 GridSettings 의존 시스템과 일관성):

```csharp
// line 43 이후:
state.RequireForUpdate<GridSettings>();
```

**OnUpdate에서 CellSize 읽기**:

```csharp
// OnUpdate 내부, line 86 이후 (ghostMap 선언 후):
float cellSize = SystemAPI.GetSingleton<GridSettings>().CellSize;
```

> RequireForUpdate로 존재가 보장되므로 TryGetSingleton 대신 GetSingleton 사용.

`cellSize`를 `ProcessRequest`에 파라미터로 전달.

```csharp
// ProcessRequest 시그니처 (line 119):
private void ProcessRequest(
    EntityCommandBuffer ecb,
    Entity workerEntity,
    Entity resourceNodeEntity,
    Entity sourceConnection,
    GatherRequestRpc rpc,
    NativeList<Entity> resourceCenters,
    float cellSize)        // 추가

// 호출부 (line 103):
ProcessRequest(ecb, workerEntity, resourceNodeEntity,
    rpcReceive.ValueRO.SourceConnection, rpc.ValueRO, resourceCenters, cellSize);
```

### 접근점 계산 변경

**위치**: `ProcessRequest` 내부, line 204-205

```csharp
// 기존:
float3 targetPos = ArrivalUtility.CalculateApproachPoint(
    workerPos, nodePos, resourceNodeEntity, in _obstacleRadiusLookup);

// 변경:
float3 targetPos = ArrivalUtility.CalculateApproachPoint(
    workerPos, nodePos, resourceNodeEntity,
    in _obstacleRadiusLookup, in _footprintLookup, cellSize);
```

---

## 3-B. HandleReturnResourceRequestSystem

**파일**: `HandleReturnResourceRequestSystem.cs`

### Lookup 추가

```csharp
// line 27 이후:
[ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
```

### OnCreate에 등록

```csharp
// line 48 이후:
_footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
```

### OnUpdate에 Update 추가

```csharp
// line 67 이후:
_footprintLookup.Update(ref state);
```

### GridSettings 싱글톤 참조

**OnCreate에 RequireForUpdate 추가**:

```csharp
// line 40 이후:
state.RequireForUpdate<GridSettings>();
```

**OnUpdate에서 CellSize 읽기**:

```csharp
// OnUpdate 내부, line 79 이후 (ghostMap 선언 후):
float cellSize = SystemAPI.GetSingleton<GridSettings>().CellSize;
```

`cellSize`를 `ProcessRequest`에 파라미터로 전달.

```csharp
// ProcessRequest 시그니처 (line 96):
private void ProcessRequest(
    EntityCommandBuffer ecb,
    Entity workerEntity,
    Entity resourceCenterEntity,
    Entity sourceConnection,
    float cellSize)        // 추가

// 호출부 (line 89):
ProcessRequest(ecb, workerEntity, resourceCenterEntity,
    rpcReceive.ValueRO.SourceConnection, cellSize);
```

### 접근점 계산 변경

**위치**: `ProcessRequest` 내부, line 169-170

```csharp
// 기존:
float3 targetPos = ArrivalUtility.CalculateApproachPoint(
    workerPos, centerPos, resourceCenterEntity, in _obstacleRadiusLookup);

// 변경:
float3 targetPos = ArrivalUtility.CalculateApproachPoint(
    workerPos, centerPos, resourceCenterEntity,
    in _obstacleRadiusLookup, in _footprintLookup, cellSize);
```

---

## 3-C. WorkerGatheringSystem 내부 접근점

**파일**: `WorkerGatheringSystem.cs`

Phase 2에서 이미 `_cellSize` 필드와 `GridSettings` 참조가 추가되어 있다. StructureFootprint Lookup만 추가로 필요.

### Lookup 추가

```csharp
// line 30 이후 (기존 Lookup 목록에 추가):
[ReadOnly] private ComponentLookup<StructureFootprint> _footprintLookup;
```

### OnCreate에 등록

```csharp
_footprintLookup = state.GetComponentLookup<StructureFootprint>(true);
```

### UpdateLookups에 추가

```csharp
_footprintLookup.Update(ref state);
```

### TransitionToReturn (line 534-535)

```csharp
// 기존:
float3 returnTargetPos = ArrivalUtility.CalculateApproachPoint(
    nodePos, centerPos, returnPoint, in _obstacleRadiusLookup);

// 변경:
float3 returnTargetPos = ArrivalUtility.CalculateApproachPoint(
    nodePos, centerPos, returnPoint,
    in _obstacleRadiusLookup, in _footprintLookup, _cellSize);
```

### DecideNextAction (line 415-416)

```csharp
// 기존:
float3 targetPos = ArrivalUtility.CalculateApproachPoint(
        workerPos, nodePos, nodeEntity, in _obstacleRadiusLookup);

// 변경:
float3 targetPos = ArrivalUtility.CalculateApproachPoint(
        workerPos, nodePos, nodeEntity,
        in _obstacleRadiusLookup, in _footprintLookup, _cellSize);
```

---

## 변경 파일 요약

| 파일 | 변경 내용 |
|------|----------|
| `HandleGatherRequestSystem.cs` | `_footprintLookup` 추가, GridSettings 참조, `CalculateApproachPoint` effectiveRadius 오버로드 사용 |
| `HandleReturnResourceRequestSystem.cs` | `_footprintLookup` 추가, GridSettings 참조, `CalculateApproachPoint` effectiveRadius 오버로드 사용 |
| `WorkerGatheringSystem.cs` | `_footprintLookup` 추가, `TransitionToReturn` + `DecideNextAction` 접근점 계산 변경 |
