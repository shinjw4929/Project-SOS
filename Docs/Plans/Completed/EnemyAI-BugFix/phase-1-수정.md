# Phase 1: Wandering 정지 수정 + FindNearestPassableCell 확대

## 목표
- Wandering 적이 목적지 도착 후 영원히 정지하는 버그 수정
- EnemyBig이 벽 안 타겟에 접근하지 못하는 버그 수정

## 선행 조건
- 없음

## 작업 목록

### Task 1: Wandering 바이패스에서 FlowFieldRef.Key 리셋
**파일**: `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` (160-179줄 부근)

현재 Wandering 바이패스:
```csharp
goal.ValueRW.IsPathDirty = false;
waypoints.ValueRW.Current = goal.ValueRO.Destination;
waypoints.ValueRW.HasNext = false;
state.EntityManager.SetComponentEnabled<MovementWaypoints>(entity, true);
```

수정: FlowFieldRef.Key = -1 리셋 추가
```csharp
goal.ValueRW.IsPathDirty = false;
waypoints.ValueRW.Current = goal.ValueRO.Destination;
waypoints.ValueRW.HasNext = false;
state.EntityManager.SetComponentEnabled<MovementWaypoints>(entity, true);
// Wandering은 FlowField를 사용하지 않으므로 Key 무효화
// → FlowFieldSteeringSystem이 이전 Chasing 키로 waypoints를 덮어쓰는 것 방지
flowFieldRefLookup[entity] = new FlowFieldRef { Key = -1 };
```

- `flowFieldRefLookup`은 현재 Apply 단계(356줄)에서만 생성됨. Wandering 바이패스 전에 일찍 생성하고, Apply 전에 `Update(ref state)` 호출

### Task 2: FindNearestPassableCell 반경 확대
**파일**: `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` (447-472줄)

현재: `for (int radius = 1; radius <= 10; radius++)`
수정: `for (int radius = 1; radius <= 30; radius++)`

30셀 근거: 그리드 CellSize=1m 기준 30m 반경. 맵 크기(100x100) 대비 충분하며, 벽으로 둘러싸인 영역 대부분 커버.

### Task 3: 탐색 실패 시 무한 루프 차단
**파일**: `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` (220-229줄)

현재: FindNearestPassableCell 실패 시 → blocked 목적지로 BFS 진행 → 전체 DirNone → 무한 루프

수정:
```csharp
if (passMap[destKey] != 0)
{
    int2 nearest = FindNearestPassableCell(destCell, passMap, gridSizeX, gridSizeY);
    if (nearest.x >= 0)
    {
        destCell = nearest;
        destKey = destCell.y * gridSizeX + destCell.x;
        isDestAdjusted = 1;
    }
    else
    {
        // 반경 30 내에도 passable 셀 없음 → BFS 시도 무의미
        // IsPathDirty=false로 무한 재요청 차단 + IsPathPartial 표기
        var g = goalLookup[entity];
        g.IsPathDirty = false;
        g.IsPathPartial = true;
        goalLookup[entity] = g;
        waypointsLookup.SetComponentEnabled(entity, false);
        continue;
    }
}
```

collect 루프에서 query는 `RefRO<MovementGoal>`이므로 직접 수정 불가. `goalLookup`과 `waypointsLookup`을 Apply 단계(354줄)가 아닌 Wandering 바이패스 전에 일찍 생성하여, collect 루프 내에서 실패 엔티티 처리에 사용. Apply 전에 `Update(ref state)` 호출하여 동기화.

## 테스트 요구사항

### 수동 검증
- EnemyBig 스폰 → 벽 안 유닛 타겟 → 벽 바깥까지 접근하여 대기
- 적 Wandering → 목적지 도착 → 즉시 새 배회 시작 (정지 없음)
- EnemyBig이 도달 불가 타겟에 대해 무한 IsPathDirty 루프 발생 안 함 (Profiler 확인)

## 검증 방법
- 컴파일 성공
- EnemyBig + 벽 둘러싸인 유닛 시나리오 인게임 테스트
- 적 4000마리 Wandering 시 서버 프레임레이트 유지

## 완료 기준
- [ ] Wandering 바이패스에서 FlowFieldRef.Key = -1 리셋
- [ ] FindNearestPassableCell 반경 30
- [ ] 탐색 실패 시 무한 루프 차단 (IsPathDirty=false, IsPathPartial=true)
- [ ] 컴파일 성공
