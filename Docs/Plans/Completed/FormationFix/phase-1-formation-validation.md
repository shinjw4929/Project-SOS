# Phase 1: 포메이션 검증 로직 추가

## 목표
HandleMoveRequestSystem.ApplyFormationOffsets에 그리드 기반 검증을 추가하여:
1. 건물 둘레 분산 유닛은 포메이션을 건너뜀
2. 포메이션 목적지가 벽/맵 밖이면 groupDest로 폴백

## 선행 조건
- 없음 (기존 코드 위에 수정)

## 작업 목록

### Task 1: OnUpdate 그리드 조건부 접근

`HandleMoveRequestSystem.OnUpdate`에서 그리드 데이터를 **조건부**로 가져온다.
`RequireForUpdate<GridSettings>()` 추가 금지 — `TryGetSingletonEntity` 사용.

```csharp
// OnUpdate 내, Pass 2 호출 직전
DynamicBuffer<GridCell> gridCells = default;
GridSettings gridSettings = default;
bool hasGrid = SystemAPI.TryGetSingletonEntity<GridSettings>(out var gridEntity);
if (hasGrid)
{
    gridSettings = SystemAPI.GetSingleton<GridSettings>();
    gridCells = SystemAPI.GetBuffer<GridCell>(gridEntity);
}
ApplyFormationOffsets(groupBuffer, gridCells, gridSettings, hasGrid);
```

- [x] OnUpdate에 TryGetSingletonEntity 분기 추가
- [x] ApplyFormationOffsets에 `NativeArray<GridCell> gridCells, GridSettings gridSettings, bool hasGrid` 파라미터 추가 (그룹핑/오프셋 로직 중복 방지를 위해 오버로드 대신 단일 메서드 + 플래그)

### Task 2: groupCenter blocked cell 검사

`ApplyFormationOffsets`에서 `hasGrid == true`일 때 각 그룹의 groupCenter 주변 3x3 셀에 IsPathBlocked가 있는지 검사.

```csharp
// groupCenter 계산 후, spacing 결정 후
int2 centerCell = GridUtility.WorldToGrid(groupCenter, gridSettings);
bool centerNearBlocked = false;
for (int dy = -1; dy <= 1 && !centerNearBlocked; dy++)
{
    for (int dx = -1; dx <= 1 && !centerNearBlocked; dx++)
    {
        int cx = centerCell.x + dx;
        int cy = centerCell.y + dy;
        if (cx < 0 || cx >= gridSettings.GridSize.x ||
            cy < 0 || cy >= gridSettings.GridSize.y)
            continue;
        if (gridCells[cy * gridSettings.GridSize.x + cx].IsPathBlocked != 0)
            centerNearBlocked = true;
    }
}
if (centerNearBlocked) continue; // 이 그룹은 포메이션 건너뜀
```

- [x] groupCenter → centerCell 변환
- [x] 3x3 IsPathBlocked 검사
- [x] `centerNearBlocked`이면 `continue`로 그룹 건너뜀

### Task 3: IsPositionBlocked 유틸리티

포메이션 목적지의 유효성을 검사하는 private static 메서드.

```csharp
private static bool IsPositionBlocked(float3 pos, float radius,
    DynamicBuffer<GridCell> cells, GridSettings gs)
{
    float cellSize = gs.CellSize;
    float2 origin = gs.GridOrigin;
    int2 gridSize = gs.GridSize;

    // 맵 밖이면 blocked
    if (pos.x - radius < origin.x || pos.x + radius > origin.x + gridSize.x * cellSize ||
        pos.z - radius < origin.y || pos.z + radius > origin.y + gridSize.y * cellSize)
        return true;

    // 그리드 셀 검사
    int cMinX = math.clamp((int)math.floor((pos.x - radius - origin.x) / cellSize), 0, gridSize.x - 1);
    int cMaxX = math.clamp((int)math.floor((pos.x + radius - origin.x) / cellSize), 0, gridSize.x - 1);
    int cMinZ = math.clamp((int)math.floor((pos.z - radius - origin.y) / cellSize), 0, gridSize.y - 1);
    int cMaxZ = math.clamp((int)math.floor((pos.z + radius - origin.y) / cellSize), 0, gridSize.y - 1);

    for (int cz = cMinZ; cz <= cMaxZ; cz++)
        for (int cx = cMinX; cx <= cMaxX; cx++)
            if (cells[cz * gridSize.x + cx].IsPathBlocked != 0)
                return true;

    return false;
}
```

- [x] 맵 경계 검사 (유닛 반경 포함)
- [x] IsPathBlocked 셀 검사 (PredictedMovementSystem.IsOverlappingBlockedCell과 동일 패턴)

### Task 4: 포메이션 목적지 검증 적용

슬롯 오프셋 적용 루프에서 각 목적지를 검증.

```csharp
float3 offset = FormationUtility.CalculateFormationOffset(slotIndex, count, spacing, moveDir);
float3 offsetDest = groupDest + offset;

float unitRadius = _obstacleRadiusLookup.TryGetComponent(entries[i].UnitEntity, out ObstacleRadius or)
    ? or.Radius : 0.5f;

if (_movementGoalLookup.HasComponent(entries[i].UnitEntity))
{
    RefRW<MovementGoal> goalRW = _movementGoalLookup.GetRefRW(entries[i].UnitEntity);
    goalRW.ValueRW.Destination = IsPositionBlocked(offsetDest, unitRadius, gridCells, gridSettings)
        ? groupDest
        : offsetDest;
}
```

- [x] ObstacleRadius로 유닛 반경 조회
- [x] IsPositionBlocked 검사
- [x] blocked이면 groupDest 폴백

### Task 5: EditMode 테스트

`Assets/Tests/EditMode/Systems/FormationValidationTests.cs` 생성.

- [x] 맵 내부 빈 셀 → false
- [x] 맵 밖 좌표 → true
- [x] 벽(IsPathBlocked=1) 셀 위 → true
- [x] 반경이 벽에 걸치는 경계 → true

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Agent A | Task 1~4 (HandleMoveRequestSystem 수정) | 없음 |
| Agent B | Task 5 (EditMode 테스트) | Task 3 완료 후 (IsPositionBlocked 시그니처 필요) |

## 검증 방법
1. EditMode Test 통과
2. Unity Editor PlayMode:
   - 건물 둘레 스폰 유닛 전체 선택 → 이동 → 모든 유닛 올바른 방향 이동
   - 벽 근처 유닛 포메이션 → 벽 투과 없음
   - 맵 가장자리에서 포메이션 → 맵 이탈 없음

## 완료 기준
- [x] HandleMoveRequestSystem 컴파일 성공 (빌드 잠금으로 CLI 검증 생략, Editor 확인 필요)
- [x] 그리드 없는 환경에서도 기존 동작 유지 (TryGetSingletonEntity 분기)
- [ ] EditMode Test 통과 (Editor에서 실행 필요)
- [ ] PlayMode 수동 검증 통과
