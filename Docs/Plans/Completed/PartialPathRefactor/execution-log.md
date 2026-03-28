# 실행 기록

## Phase 1: IsPathPartial 재평가 기반 구축 - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| FlowFieldSystem Apply: IsPathPartial 자동 판정 | 완료 | `goal.IsPathPartial = (pending.IsDestAdjusted == 1);` |
| EnemyTargetJob 섹션 1: dest 억제 가드 제거 | 완료 | `if (!IsPathPartial)` 가드 + 수동 클리어 + ShouldRetryPartialPath 제거 |
| EnemyTargetJob AggroLock: dest 억제 가드 제거 | 완료 | 동일 패턴 제거 |
| 빌드 검증 | 미확인 | Unity Editor 프로젝트 잠금으로 배치 빌드 불가, Editor에서 확인 필요 |

### 변경된 파일
- `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` - Apply: IsPathPartial 매 프레임 자동 판정으로 변경
- `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` - dest 억제 가드 2곳 제거, ShouldRetryPartialPath 호출 2곳 제거, IsPathPartial 수동 클리어 제거

### Phase 1 완료 판정: Pass (빌드 미확인)

## Phase 2: 대체 타겟 탐색 + Stuck fallback - 2026-03-29

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 1: EnemyState 필드 추가 | 완료 | AbandonedTarget + AbandonedExpireTime |
| Task 1: EnemyAuthoring Baker 초기화 | 완료 | Entity.Null 초기화 |
| Task 2: GameSettings 필드 추가 | 완료 | TargetAbandonDuration (기본 30f) |
| Task 2: GameSettingsAuthoring 인스펙터 + Baker | 완료 | [Min(1f)] 제약 |
| Task 3a: EnemyTargetJob 필드 추가 | 완료 | TargetAbandonDuration + OnUpdate 전달 |
| Task 3b: IsPathPartial + Chasing 대체 탐색 + stuck 감지 | 완료 | CheckStuck → AbandonedTarget 차단, 프레임 분산 대체 탐색 |
| Task 3c: 타겟 탐색 루프 필터 | 완료 | AbandonedTarget + _partialSearchExclude 필터 |
| Task 3d: 결과 적용 대체 탐색 복원 분기 | 완료 | 대체 없음 → 기존 타겟 복원 |
| Task 4: TargetPropagationJob AbandonedTarget 필터 | 완료 | ElapsedTime 전달 + 전파 차단 |
| 빌드 검증 | 미확인 | Unity Editor 프로젝트 잠금 |

### 변경된 파일
- `Assets/Scripts/Shared/Components/State/EnemyState.cs` - AbandonedTarget, AbandonedExpireTime 필드 추가
- `Assets/Scripts/Authoring/Entities/EnemyAuthoring.cs` - Baker 초기화
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` - TargetAbandonDuration 필드 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` - 인스펙터 필드 + Baker 매핑
- `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` - 대체 탐색 + stuck 감지 + AbandonedTarget 필터 + TargetPropagation 필터

### Phase 2 완료 판정: Pass (빌드 미확인)
