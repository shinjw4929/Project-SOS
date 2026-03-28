# Phase 1: IsPathPartial 재평가 기반 구축

## 목표

dest를 항상 targetPos로 갱신하고, FlowFieldSystem이 매 프레임 도달 가능 여부를 재평가하도록 한다. IsPathPartial 영구 고착 문제를 해결한다.

## 선행 조건

없음

## 작업 목록

### Task 1: FlowFieldSystem Apply — IsPathPartial 클리어

**파일**: `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs`
**위치**: `ApplyFlowFieldResults` 메서드, L661-662 (성공 경로)

- [ ] `IsDestAdjusted==0`일 때 `IsPathPartial=false` 클리어 추가:

```csharp
// 변경 전
if (pending.IsDestAdjusted == 1)
    goal.IsPathPartial = true;

// 변경 후
goal.IsPathPartial = (pending.IsDestAdjusted == 1);
```

**동작**: BFS 성공 + 목적지 미조정 → IsPathPartial=false (타겟이 벽 밖). 목적지 조정됨 → IsPathPartial=true (타겟이 벽 안).

### Task 2: EnemyTargetJob — dest 억제 제거 + ShouldRetryPartialPath 제거

**파일**: `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs`

**2a. 섹션 1 dest 억제 제거** (L297-318):

- [ ] `if (!goal.ValueRO.IsPathPartial)` 가드 제거, dest를 항상 갱신:

```csharp
// 변경 전
if (math.distancesq(currentDest, targetPos) > DestinationThresholdSq)
{
    if (!goal.ValueRO.IsPathPartial)
    {
        goal.ValueRW.Destination = targetPos;
        goal.ValueRW.IsPathDirty = true;
        goal.ValueRW.DestinationSetTime = ElapsedTime;
    }
}

if (MovementMath.ShouldRetryPartialPath(...))
{
    goal.ValueRW.Destination = targetPos;
    goal.ValueRW.IsPathDirty = true;
    goal.ValueRW.DestinationSetTime = ElapsedTime;
}

// 변경 후
if (math.distancesq(currentDest, targetPos) > DestinationThresholdSq)
{
    goal.ValueRW.Destination = targetPos;
    goal.ValueRW.IsPathDirty = true;
    goal.ValueRW.DestinationSetTime = ElapsedTime;
}
```

**2b. AggroLock 섹션 동일 처리** (L232-250):

- [ ] `if (!goal.ValueRO.IsPathPartial)` 가드 제거 + ShouldRetryPartialPath 호출 제거:

```csharp
// 변경 전
if (math.distancesq(currentDest, lockedPos) > DestinationThresholdSq)
{
    goal.ValueRW.Destination = lockedPos;
    goal.ValueRW.IsPathDirty = true;
    if (!goal.ValueRO.IsPathPartial)
    {
        goal.ValueRW.DestinationSetTime = ElapsedTime;
    }
}

if (MovementMath.ShouldRetryPartialPath(...))
{
    goal.ValueRW.IsPathDirty = true;
    goal.ValueRW.DestinationSetTime = ElapsedTime;
}

// 변경 후
if (math.distancesq(currentDest, lockedPos) > DestinationThresholdSq)
{
    goal.ValueRW.Destination = lockedPos;
    goal.ValueRW.IsPathDirty = true;
    goal.ValueRW.DestinationSetTime = ElapsedTime;
}
```

**2c. IsPathPartial 클리어 조건 정리** (L290-295):

- [ ] 기존 `distancesq(goal.Destination, targetPos) < threshold` 클리어 조건 제거 (FlowFieldSystem Apply에서 처리하므로 불필요):

```csharp
// 삭제
if (goal.ValueRO.IsPathPartial
    && math.distancesq(goal.ValueRO.Destination, targetPos) < DestinationThresholdSq)
{
    goal.ValueRW.IsPathPartial = false;
}
```

## 테스트 요구사항

### 수동 테스트
1. EnemyBig → 벽 안 타겟 → 벽 경계까지 이동 확인 (기존 동작 유지)
2. 타겟이 벽 밖으로 이동 → 즉시 추격 재개 (IsPathPartial=false 전환)
3. 벽 파괴 → 즉시 추격 재개

## 검증 방법

- `/build` 성공
- 타겟 이동 시 즉시 반응 (2초 대기 없음)
- wallEdge 도달 후 정상 대기 (영구 정지 아님 — 매 프레임 BFS 재평가)

## 완료 기준

- [ ] FlowFieldSystem Apply에서 IsDestAdjusted==0일 때 IsPathPartial=false 클리어
- [ ] EnemyTargetJob dest 억제 가드 제거
- [ ] ShouldRetryPartialPath 호출 2곳 제거
- [ ] 기존 IsPathPartial 클리어 조건 제거
- [ ] 컴파일 성공
