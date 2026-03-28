# Phase 2: 대체 타겟 탐색 + Stuck fallback

## 목표

벽 앞 정지 적이 근처 유닛에 즉시 반응하고, 대체 타겟이 없으면 일정 시간 후 타겟을 임시 차단하고 Wandering으로 전환한다.

## 선행 조건

Phase 1 완료 (dest 항상 갱신, IsPathPartial 자동 클리어)

## 작업 목록

### Task 1: EnemyState 필드 추가

**파일**: `Assets/Scripts/Shared/Components/State/EnemyState.cs`

- [ ] `Entity AbandonedTarget` 필드 추가 (임시 차단 타겟, Entity.Null = 없음)
- [ ] `float AbandonedExpireTime` 필드 추가 (차단 만료 시각)

**파일**: `Assets/Scripts/Authoring/Entities/EnemyAuthoring.cs`

- [ ] Baker에서 `AbandonedTarget = Entity.Null` 초기화

### Task 2: GameSettings 필드 추가

**파일**: `Assets/Scripts/Shared/Singletons/GameSettings.cs`

- [ ] `float TargetAbandonDuration` 필드 추가 (적 AI 섹션)

**파일**: `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs`

- [ ] `targetAbandonDuration` 인스펙터 필드 (기본 30f) + Baker 매핑

### Task 3: 대체 타겟 탐색

**파일**: `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs`

**3a. EnemyTargetJob 필드 추가**:
- [ ] `float TargetAbandonDuration`, `float StuckCheckInterval`, `float StuckThreshold`
- [ ] OnUpdate에서 GameSettings 전달

**3b. 섹션 1: IsPathPartial + Chasing 대체 탐색** (dest 갱신 후, `if (!needNewTarget) return;` 전):

```csharp
// IsPathPartial: 프레임 분산 대체 타겟 탐색 + stuck 감지
if (goal.ValueRO.IsPathPartial && enemyState.ValueRO.CurrentState == EnemyContext.Chasing)
{
    // stuck 감지 (~6초)
    if (WanderUtility.CheckStuck(in myPos, goal.ValueRO.LastPositionCheck,
            goal.ValueRO.LastPositionCheckTime, ElapsedTime, out bool isStuck,
            StuckCheckInterval, StuckThreshold))
    {
        if (isStuck)
        {
            enemyState.ValueRW.AbandonedTarget = currentTarget;
            enemyState.ValueRW.AbandonedExpireTime = ElapsedTime + TargetAbandonDuration;
            target.ValueRW.TargetEntity = Entity.Null;
            goal.ValueRW.IsPathPartial = false;
            needNewTarget = true;
        }
        goal.ValueRW.LastPositionCheckTime = ElapsedTime;
        goal.ValueRW.LastPositionCheck = myPos;
    }

    // 프레임 분산 대체 타겟 탐색 (4프레임 1회)
    if (!needNewTarget)
    {
        uint frameSlice = FrameCount % TimeSliceDivisor;
        if ((uint)entity.Index % TimeSliceDivisor == frameSlice)
        {
            _partialSearchExclude = currentTarget;
            needNewTarget = true;
        }
    }
}
```

`_partialSearchExclude`: Execute 시작 시 `Entity.Null`로 초기화하는 Job 로컬 필드.

**3c. 타겟 탐색 루프에 필터 추가** (L400-419 내):

```csharp
// 임시 차단 타겟 필터
if (candidate.Entity == enemyState.ValueRO.AbandonedTarget
    && ElapsedTime < enemyState.ValueRO.AbandonedExpireTime)
    continue;

// 대체 탐색: 현재 도달 불가 타겟 제외
if (candidate.Entity == _partialSearchExclude)
    continue;
```

**3d. 결과 적용에 대체 탐색 복원 분기** (L429 이후):

```csharp
if (bestTarget != Entity.Null)
{
    // 대체 타겟 발견 → 전환
    target.ValueRW.TargetEntity = bestTarget;
    // ... 기존 Chasing 로직 ...
    if (previousTarget != bestTarget)
        goal.ValueRW.IsPathPartial = false; // 새 타겟은 재평가
}
else if (_partialSearchExclude != Entity.Null)
{
    // 대체 없음 → 기존 타겟 복원
    target.ValueRW.TargetEntity = _partialSearchExclude;
    enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
    return;
}
else { ... Wandering ... }
```

### Task 4: TargetPropagationJob AbandonedTarget 필터

**파일**: `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs`

- [ ] TargetPropagationJob에 `float ElapsedTime` 필드 추가 + OnUpdate 전달
- [ ] 전파 적용 전 AbandonedTarget 필터:
```csharp
if (bestTarget == enemyState.AbandonedTarget
    && ElapsedTime < enemyState.AbandonedExpireTime)
    return;
```

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 + Task 2 (컴포넌트 + 설정) | 없음 |
| 순차 | Task 3 + Task 4 (로직 — 같은 파일) | Agent A |

## 테스트 요구사항

### 수동 테스트
1. 벽 앞 적 + 다른 유저 유닛 접근 → 4프레임 내 대체 전환
2. 대체 타겟 없음 + ~6초 → AbandonedTarget 차단 + Wandering
3. 30초 후 차단 만료 → 재획득 가능
4. TargetPropagation이 차단된 타겟 재전파 안 함

## 완료 기준

- [ ] EnemyState.AbandonedTarget/AbandonedExpireTime 필드 존재
- [ ] GameSettings.TargetAbandonDuration 필드 존재
- [ ] IsPathPartial && Chasing 시 프레임 분산 대체 탐색 동작
- [ ] stuck 감지 → AbandonedTarget 차단 + Wandering
- [ ] 타겟 탐색 + TargetPropagation에서 AbandonedTarget 필터 동작
- [ ] 컴파일 성공
