# Phase 4: 적 타겟 탐색 개선

## 목표
- 적이 아군 유닛을 더 잘 찾도록 Wandering 방향 편향 + 탐색 주기 조정
- 적이 산발적으로 퍼지지 않고 아군 기지 방향으로 수렴

## 선행 조건
- 없음 (Phase 1, 2와 병렬 가능)

## 문제 분석

### 현재 Wandering 방향
- `WanderUtility.GenerateWanderDestination`: **맵 전체 범위** 내 랜덤 좌표
- 적이 타겟을 못 찾으면 맵 구석으로 퍼져나감
- 아군 기지 반대 방향으로 가면 더 멀어져서 영원히 못 찾음

### 현재 탐색 주기
- `TimeSliceDivisor = 4`: 적은 4프레임 중 1프레임만 탐색
- 나머지 3프레임은 즉시 Wandering 전환
- 적이 처음 스폰되었을 때도 75% 확률로 즉시 Wandering → 첫 탐색 전에 엉뚱한 방향으로 이동 시작

### 현재 탐색 범위
- TargetingMap CellSize = 10.0f
- 탐색 반경 = ceil(visionRange * HysteresisMultiplier / CellSize)
- EnemySmall visionRange=10 → 탐색 반경 = ceil(13/10) = 2셀 = 20m
- 맵에서 아군이 20m 밖이면 영원히 못 찾음 → Wandering 지속

## 작업 목록

### Task 1: Wandering 방향 편향
- [ ] `WanderUtility.GenerateWanderDestination` 수정:
  - 기존: 맵 전체 랜덤
  - 변경: 맵 중심(또는 아군 기지 좌표) 방향으로 편향
  - 구현: `wanderDest = currentPos + biasedDirection * wanderDistance`
    - `biasedDirection = normalize(lerp(randomDir, toCenter, biasFactor))`
    - `biasFactor`: GameSettings에서 설정 (기본 0.5 = 50% 편향)
    - `wanderDistance`: 10~20m 범위 (맵 전체 대신 근거리)
- [ ] `GameSettings`에 `WanderBiasFactor` (기본 0.5), `WanderMaxDistance` (기본 20.0f) 추가
- [ ] `GameSettingsAuthoring` 대응 필드 추가
- [ ] 호출처 9곳 업데이트: `UnifiedTargetingSystem` 3곳 + `WanderUtilityTests` 6곳

### Task 2: 최초 스폰 즉시 탐색
- [ ] `UnifiedTargetingSystem.EnemyTargetJob` 수정:
  - 적이 **한 번도 타겟을 찾지 않은 상태**에서는 TimeSlice 무시하고 즉시 탐색
  - 구현: `EnemyState`에 `HasEverFoundTarget` bool 추가 (또는 기존 `AggroTarget.TargetEntity` 이력 활용)
  - 간단한 방법: 적이 처음 `EnemyContext.Idle`에서 시작 → Idle 상태에서는 TimeSlice 무시
  ```
  // 현재
  bool isMySearchFrame = ((uint)entity.Index % TimeSliceDivisor) == frameSlice;
  if (!isMySearchFrame) { ... wandering ... }

  // 변경
  bool isIdle = enemyState.CurrentState == EnemyContext.Idle;
  bool isMySearchFrame = isIdle || ((uint)entity.Index % TimeSliceDivisor) == frameSlice;
  ```

### Task 3: TargetingMap 셀 크기 조정 (선택)
- [ ] `SpatialHashUtility.TargetingCellSize`: 10.0f → 8.0f 고려
  - 장점: visionRange 10 기준 탐색 반경 2셀(16m) → 더 정밀
  - 단점: 메모리 약간 증가, 빌드 시 해시 엔트리 증가
  - **판단**: 프로파일링 결과에 따라 결정, Phase 4에서는 10.0f 유지하고 편향만 적용
  - 필요 시 후속 작업으로 분리

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (Wandering 편향) | 없음 |
| Agent B | Task 2 (즉시 탐색) | 없음 |
| Main | Task 3 (선택적 셀 크기 조정) | Task 1, 2 완료 후 |

## 테스트 요구사항

### EditMode Test
- `WanderUtility.GenerateWanderDestination` (편향 버전):
  - biasFactor=1.0 → 항상 중심 방향
  - biasFactor=0.0 → 기존과 동일 (랜덤)
  - 생성된 좌표가 wanderMaxDistance 이내
  - 그리드 범위 내 유효 좌표

### PlayMode Test
- 적 100마리 스폰 (맵 가장자리) → 30초 후 맵 중심 방향 이동 비율 확인
- 적 50마리 아군 유닛 1개 근처 스폰 → 10초 내 타겟 감지 비율

## 검증 방법
1. 적 100마리 스폰 → 30초 후 맵 중심 반경 30m 이내 적 비율 > 50%
2. 적 스폰 직후 Idle → Chasing 전환 시간 < 2초 (visionRange 내 아군 존재 시)
3. 기존 전투 동작 회귀 없음

## 완료 기준
- [ ] 컴파일 성공
- [ ] EditMode Test 통과
- [ ] 적이 아군 방향으로 수렴하는 경향 확인
- [ ] 스폰 직후 즉시 타겟 탐색
