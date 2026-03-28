# 적 그룹 타겟 전파 + Steering TimeSlice 오케스트레이션 플랜

## 문제 정의

1. **타겟 탐색 병목**: 4000마리 적이 각자 개별 타겟 탐색 (SpatialMap 순회) → TimeSliceDivisor=4로도 프레임당 1000회 탐색. 탐색 비용이 엔티티 수에 선형 비례.
2. **Steering 회피 병목**: 4000마리가 매 프레임 3x3 셀 이웃과 Steering 계산 → O(N*neighbors) 연산 + 방향 진동으로 떨림 발생.

### 영향 범위
- **Server**: UnifiedTargetingSystem (EnemyTargetJob), PredictedMovementSystem (CalculateSteeringAvoidance)
- **Shared**: GameSettings, 신규 컴포넌트 (CachedAvoidanceDir)

---

## AS-IS (현재 상태)

### 적 타겟 탐색 흐름
```
EnemyTargetJob (IJobEntity, ScheduleParallel)
  ↓ 각 적마다 독립 탐색
  ↓ TargetingMap 3x3~5x5 셀 순회 (visionRange 기준)
  ↓ 최근접 아군 발견 → AggroTarget 설정, EnemyContext.Chasing
  ↓ 미발견 → EnemyContext.Wandering + 랜덤 배회
```

- **TimeSliceDivisor=4**: 적의 25%만 매 프레임 탐색
- 나머지 75%는 탐색 건너뛰고 즉시 Wandering 전환
- **비효율**: 적 A가 타겟을 찾았어도, 바로 옆 적 B는 모르고 다음 탐색 프레임까지 Wandering

### Steering 회피 연산
- `PredictedMovementSystem.CalculateSteeringAvoidance`: 매 프레임 모든 엔티티가 3x3 셀 이웃 탐색
- 4000 적이 각각 10~20개 이웃 체크 → 40,000~80,000 연산/프레임
- 매 프레임 방향 재계산 → 밀집 시 방향 진동 (떨림)

### 관련 파일
| 파일 | 역할 |
|------|------|
| `Server/Systems/Combat/UnifiedTargetingSystem.cs` | EnemyTargetJob + UnitAutoTargetJob |
| `Server/Systems/Movement/PredictedMovementSystem.cs` | CalculateSteeringAvoidance |
| `Shared/Components/State/EnemyState.cs` | EnemyContext enum |
| `Shared/Singletons/Ref/AggroTarget.cs` | 타겟 추적 컴포넌트 |

---

## TO-BE (목표 상태)

### 2패스 타겟팅
```
Pass 1: EnemyTargetJob (기존, 변경 없음)
  ↓ 일부 적이 타겟 발견 → AggroTarget 설정
  ↓
Pass 2: TargetPropagationJob (신규)
  ↓ 타겟 없는 적(Wandering/Idle)이 주변 적의 AggroTarget을 확인
  ↓ 인접 적에게 유효 타겟이 있으면 복사
  ↓ EnemyContext.Chasing 전환 + MovementGoal 설정
```

### 핵심 설계
- **전파 범위**: MovementMap 3x3 셀 (CellSize=3.0f → 반경 ~9m)
- **전파 조건**: 자신이 타겟 없음 + 이웃 적이 유효 타겟 보유 + 타겟이 살아있음
- **전파 후**: AggroTarget 복사 + EnemyState → Chasing + MovementGoal.Destination = 타겟 위치
- **Job 안전성**: Pass 1 완료 후 Pass 2 실행 (dependency chain). Pass 2는 AggroTarget을 읽기+쓰기하지만, 타겟 없는 엔티티만 쓰기 → 충돌 없음

### 성능 효과 (타겟 전파)
- 적 A가 타겟 발견 → 주변 적 ~10마리가 즉시 같은 타겟 획득
- 다음 프레임: 해당 10마리 주변의 또 다른 적에게 전파 (연쇄)
- **실효 탐색 비용**: 1/10 이하로 감소

### Steering TimeSlice + 캐시 방향
```
CalculateSteeringAvoidance 변경:
  if (entityIndex % SteeringSliceDivisor == frameCount % SteeringSliceDivisor):
    → 이웃 탐색 실행, 회피 방향 계산 + CachedAvoidanceDir에 저장
  else:
    → CachedAvoidanceDir에서 캐시된 방향 재사용 (이웃 탐색 skip)
```
- **SteeringSliceDivisor=4**: 매 프레임 25%만 이웃 탐색 → 연산 4배 감소
- 캐시된 방향을 매 프레임 적용하므로 회피 자체는 끊기지 않음 → 겹침 방지 유지
- 4프레임 동안 방향 고정 → 떨림 감소

### 성능 효과 (Steering TimeSlice)
- 이웃 탐색: 40,000~80,000 → 10,000~20,000 연산/프레임
- 떨림: 매 프레임 방향 전환 → 4프레임 고정 방향

---

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| 타겟 탐색 | 각 적 개별 수행 | 개별 탐색 + 주변 전파 |
| 탐색 실패 시 | 즉시 Wandering | 주변 적 타겟 복사 시도 → 실패 시 Wandering |
| 프레임당 탐색 수 | N/TimeSliceDivisor | 동일 (전파는 추가 비용이지만 SpatialMap 조회만) |
| 반응 속도 | 타겟 발견까지 최대 4프레임 | 주변 적이 발견하면 1프레임 후 전파 |
| Steering 계산 | 매 프레임 모든 엔티티 | 25%만 계산 + 75% 캐시 재사용 |
| 떨림 | 매 프레임 방향 전환 | 4프레임 고정 방향 |

---

## Phase 체크리스트

### Phase 1: TargetPropagationJob 구현
- [x] `UnifiedTargetingSystem`에 `TargetPropagationJob` 추가
- [x] EnemyTargetJob 후 dependency chain으로 실행
- [x] 전파 범위/조건 구현
- [x] GameSettings에 `TargetPropagationRadius` 추가 (기본: 9.0f)
> 상세: [phase-1-전파구현.md](./phase-1-전파구현.md)

### Phase 2: Steering TimeSlice + 캐시 방향
- [x] `CachedAvoidanceDir` 컴포넌트 추가 (float3 Direction + float Strength)
- [x] `CalculateSteeringAvoidance`에 TimeSlice 로직 적용
- [x] GameSettings에 `SteeringSliceDivisor` 추가 (기본: 4)
- [x] Authoring: `MovementAuthoring` Baker에서 `CachedAvoidanceDir` 부착
> 상세: [phase-2-steering-timeslice.md](./phase-2-steering-timeslice.md)

---

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|-----------|
| 1 | 없음 | - |
| 2 | 없음 | O (Phase 1과 병렬) |

---

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `UnifiedTargetingSystem.cs` | TargetPropagationJob 추가, OnUpdate 스케줄링 변경 |
| 1 | `GameSettings.cs` | TargetPropagationRadius 추가 |
| 1 | `GameSettingsAuthoring.cs` | 대응 필드 추가 |
| 2 | `PredictedMovementSystem.cs` | CalculateSteeringAvoidance TimeSlice + 캐시 |
| 2 | `CachedAvoidanceDir.cs` (신규) | float3 캐시 컴포넌트 |
| 2 | `MovementAuthoring.cs` | Baker에서 CachedAvoidanceDir 부착 |
| 2 | `GameSettings.cs` | SteeringSliceDivisor 추가 |
| 2 | `GameSettingsAuthoring.cs` | 대응 필드 추가 |

---

## 검증 방법

1. 적 100마리 스폰 + 아군 1개 → 1마리가 발견 후 주변 적에게 전파되는지 확인
2. Profiler: EnemyTargetJob + TargetPropagationJob 합산 < 기존 EnemyTargetJob 단독
3. 적 4000마리 배회 → PredictedMovementSystem 프레임 시간 50% 감소
4. 적 밀집 시 떨림 없음 (4프레임 고정 방향)
5. 겹침/밀림 없음 유지

---

## 롤백 전략

- **Phase 1**: TargetPropagationJob 스케줄링 제거, 기존 handle chain 복원, GameSettings 필드 제거
- **Phase 2**: TimeSlice 조건 제거 (매 프레임 계산 복원), CachedAvoidanceDir 컴포넌트 제거
