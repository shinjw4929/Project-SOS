# Phase 1: FlowField 확장 + Wandering 분리

## 목표
- FlowField BFS 캐시 병목 해소 (32 → 128)
- Wandering 적 4000마리가 FlowField 캐시를 소진하지 않도록 직선 이동 분리
- 프레임당 BFS 계산 수 상한 도입으로 프레임 스파이크 방지
- PredictedMovementSystem ISystem [BurstCompile] 누락 수정

## 선행 조건
- 없음 (첫 번째 Phase)

## 작업 목록

### Task 1: MaxFields 확대
- [ ] `FlowFieldSystem.cs`: `const int MaxFields = 32` → `128`
- [ ] 메모리 영향 확인: 100x100 그리드 기준 FieldPool = 128 * 10000 * 1byte * 2 = 2.5MB (허용 범위)

### Task 2: Wandering 적 FlowField 바이패스
- [ ] `FlowFieldSystem.OnUpdate()` Collect 단계에서 Wandering 적 분리
  - `EnemyState` ComponentLookup 추가 (ReadOnly)
  - `EnemyContext.Wandering` 상태인 적: `IsPathDirty=false`, Waypoints.Current = Destination, SetComponentEnabled true
  - 기존 지상 유닛 수집 루프에서 Wandering 적 skip (enemyStateLookup 체크)
- [ ] Wandering 적은 `PredictedMovementSystem`의 벽 슬라이딩으로 장애물 처리
- [ ] 기존 StuckCheck → Dormant 전환 로직 그대로 유지

### Task 3: PredictedMovementSystem [BurstCompile] 누락 수정
- [ ] `PredictedMovementSystem` ISystem 레벨에 `[BurstCompile]` 추가
- [ ] 현재 KinematicMovementJob에만 적용되어 있고 시스템 자체는 누락

> FlowFieldSteeringSystem IJobEntity 전환은 검토 결과 불필요로 판단.
> 워크로드가 단순 배열 조회 수준이라 병렬화 이점 없음. 이미 [BurstCompile] 적용.

### Task 4: 프레임당 BFS 상한 도입
- [ ] `GameSettings`에 `MaxBFSPerFrame` 필드 추가 (기본값: 16)
- [ ] `GameSettingsAuthoring`에 대응 필드 추가
- [ ] `FlowFieldSystem` Collect 단계: 캐시 miss 수가 `MaxBFSPerFrame`을 초과하면 나머지는 다음 프레임으로 연기
  - 초과분 유닛의 `IsPathDirty`는 true 유지 (다음 프레임에 재수집)
  - SmallMiss + LargeMiss 합산으로 제한

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (MaxFields) + Task 2 (Wandering 바이패스) + Task 4 (BFS 상한) | 없음 (동일 파일) |
| Agent B | Task 3 ([BurstCompile] 수정) | 없음 |

## 테스트 요구사항

### EditMode Test
- `WanderUtility` 기존 테스트 유지 (변경 없음)
- BFS 상한 로직 단위 테스트: miss 수 > MaxBFSPerFrame 시 초과분 skip 확인

### PlayMode Test (필요 시)
- 4000 EnemySmall 스폰 → FlowFieldSystem 프레임 시간 측정
- Wandering 적이 직선 이동으로 정상 배회하는지 확인
- PredictedMovementSystem [BurstCompile] 적용 후 정상 동작 확인

## 검증 방법
1. Unity Profiler: FlowFieldSystem < 2ms / FlowFieldSteeringSystem < 1ms (4000 엔티티)
2. Wandering 적이 정상적으로 배회 + 벽에 부딪히면 슬라이딩
3. 비 Wandering 적(Chasing/Attacking)의 경로탐색 정상 동작

## 완료 기준
- [ ] 컴파일 성공
- [ ] 4000 엔티티 시 서버 프레임 시간 < 5ms (FlowField 관련)
- [ ] Wandering 적 배회 정상
- [ ] 기존 유닛/적 이동 동작 회귀 없음
