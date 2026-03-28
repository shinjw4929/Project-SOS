# 실행 기록

## Phase 1: FlowField 확장 + Wandering 분리 - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: MaxFields 32→128 | 완료 | 단일 상수 변경 |
| Task 2: Wandering 적 직선 이동 바이패스 | 완료 | EnemyStateLookup 추가, Collect 전 별도 루프 |
| Task 3: PredictedMovementSystem [BurstCompile] | 완료 | ISystem struct 레벨에 추가 |
| Task 4: BFS 상한 (MaxBFSPerFrame) | 완료 | GameSettings 필드 + Collect 루프 내 카운트 제한 |

### 변경된 파일
- `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` — MaxFields 128, Wandering 바이패스, BFS 상한
- `Assets/Scripts/Server/Systems/Movement/PredictedMovementSystem.cs` — [BurstCompile] 추가
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` — MaxBFSPerFrame 필드 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` — maxBFSPerFrame 필드 + Baker 매핑

### 발견된 이슈
- 없음

### Phase 1 완료 판정: Pass (컴파일 검증 필요 — 사용자 Unity Editor 확인)

---

## Phase 2: 충돌 모델 전환 (Separation → Steering) - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: CalculateSeparation → CalculateSteeringAvoidance | 완료 | 방향만 조정, 위치 밀림 없음 |
| Task 2: MovementArrivalSystem 2차 판정 | 완료 | 저속 감지로 대체 |
| Task 3: GameSettings 파라미터 변경 | 완료 | Separation 3개 → Avoidance 2개 |
| Task 4: 벽 투과 방지 강화 | 완료 | 이동 전 검증 + 축별 분리 + ClampToWall 5회 |
| Task 5: 주석/문서 정리 | 완료 | FlowFieldSteering 주석, Architecture.md |

### 변경된 파일
- `Assets/Scripts/Server/Systems/Movement/PredictedMovementSystem.cs` — Separation→Steering 교체, 벽 검증 강화, ClampToWall 5회
- `Assets/Scripts/Server/Systems/Movement/MovementArrivalSystem.cs` — 2차 판정 저속 기반
- `Assets/Scripts/Server/Systems/Movement/FlowFieldSteeringSystem.cs` — Separation 주석 수정
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` — Separation→Avoidance 파라미터
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` — 대응 필드 변경
- `Docs/Architecture.md` — GameSettings 카테고리 업데이트

### 발견된 이슈
- EntitiesSubScene.unity에 기존 Separation 파라미터 값이 남아있음 → Unity Editor에서 GameSettingsAuthoring Inspector 재설정 후 씬 저장 필요

### Phase 2 완료 판정: Pass (컴파일 + 씬 리베이크 필요)

---

## Phase 3: 군집 이동 (Group Formation) - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: HandleMoveRequestSystem 그룹 감지 + 오프셋 | 완료 | 2패스: 수집 → 그룹핑(소유자+목적지 1m이내) → FormationUtility 오프셋 |
| Task 2: FormationUtility 신규 + EditMode Test | 완료 | sqrt(N)xsqrt(N) 격자, 이동 방향 회전, 6개 테스트 |
| Task 3: 도착 지점 오프셋 | 자동 대응 | Destination에 오프셋 적용 → MovementArrivalSystem 기존 로직으로 도착 판정 |

### 변경된 파일
- `Assets/Scripts/Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` — 2패스 그룹핑 + FormationUtility 오프셋 적용
- `Assets/Scripts/Shared/Utilities/FormationUtility.cs` — 신규: 격자 대형 오프셋 계산
- `Assets/Tests/EditMode/Utilities/FormationUtilityTests.cs` — 신규: 6개 EditMode 테스트

### 발견된 이슈
- 없음

### Phase 3 완료 판정: Pass (컴파일 검증 필요)

---

## Phase 4: 적 타겟 탐색 개선 - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: Wandering 방향 편향 | 완료 | 맵 중심 편향, biasFactor/wanderMaxDistance 파라미터화 |
| Task 2: 최초 스폰 즉시 탐색 | 완료 | Idle 상태에서 TimeSlice 무시 |
| Task 3: TargetingMap 셀 크기 | 보류 | 프로파일링 후 결정 |

### 변경된 파일
- `Assets/Scripts/Shared/Utilities/WanderUtility.cs` — 편향 배회 목적지 생성 (currentPos 기반, biasFactor/wanderMaxDistance)
- `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` — 호출처 3곳 업데이트, Job 필드 추가, Idle 즉시 탐색
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` — WanderBiasFactor, WanderMaxDistance 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` — 대응 필드 + Baker 매핑
- `Assets/Tests/EditMode/Utilities/WanderUtilityTests.cs` — 호출처 6곳 시그니처 업데이트

### 발견된 이슈
- 없음

### Phase 4 완료 판정: Pass (컴파일 검증 필요)
