# 실행 기록

## 계획 수정 - 2026-04-01

### 수정 사유
- CombatTiltSystem이 Phase 2에서 사망 기울임까지 담당하게 되어 "Combat" 접두사가 범위를 정확히 반영하지 못함

### 수정 내역
| 항목 | 변경 전 | 변경 후 |
|---|---|---|
| Phase 2 Task 2 | CombatTiltSystem 확장 | CombatTiltSystem → EntityTiltSystem 리네임 + 확장 |
| orchestration.md | 다른 계획 관계 없음 | CombatTiltFix 교차 참조 추가 |

### 영향받는 Phase
- Phase 2: Task 2에 리네임 작업 추가, 완료 기준에 리네임 항목 추가

### 다른 계획 갱신
- `Docs/Plans/CombatTiltFix/orchestration.md`: "다른 계획과의 관계" 섹션에 리네임 및 실행 순서별 조정 사항 갱신

---

## Phase 1: 서버 사망 지속시간 도입 - 2026-04-01

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 1: DeathTimer 컴포넌트 생성 | 완료 | `Shared/Components/State/DeathTimer.cs` 신규, InitialWallDecayTimer 패턴 준수 |
| Task 2: GameSettings DeathDuration 추가 | 완료 | GameSettings + GameSettingsAuthoring + Baker 매핑, 기본값 0.5초 |
| Task 3: ServerDeathSystem 리팩토링 | 완료 | DeathDetectionJob([WithNone(DeathTimer)]) + DeathTimerJob 2-Job 분리 |
| Task 4: 전투 시스템 Dying/Dead 스킵 | 완료 | MeleeAttackSystem 2개 Job + RangedAttackSystem 2개 Job에 early return 추가 |
| Task 5: PredictedMovementSystem 확장 | 완료 | isEnemyAttacking/isUnitAttacking → isEnemyInactive/isUnitInactive로 Dying/Dead 포함 |

### 변경된 파일
- `Assets/Scripts/Shared/Components/State/DeathTimer.cs` - 신규: 사망 연출 타이머 컴포넌트
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` - DeathDuration 필드 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` - deathDuration 인스펙터 필드 + Baker 매핑 추가
- `Assets/Scripts/Server/Systems/Combat/ServerDeathSystem.cs` - ServerDeathJob → DeathDetectionJob + DeathTimerJob 분리
- `Assets/Scripts/Server/Systems/Combat/MeleeAttackSystem.cs` - EnemyMeleeAttackJob, UnitMeleeAttackJob에 Dying/Dead early return
- `Assets/Scripts/Server/Systems/Combat/RangedAttackSystem.cs` - RangedUnitAttackJob, RangedEnemyAttackJob에 Dying/Dead early return
- `Assets/Scripts/Server/Systems/Movement/PredictedMovementSystem.cs` - Dying/Dead 상태 이동 스킵 추가

### 발견된 이슈
- 없음

### Phase 1 완료 판정: Pass

---

## Phase 2: Dying 상태 보호 + 클라이언트 사망 기울임 - 2026-04-05

### 1차 시도 교훈 (2026-04-01)
- lerp 기반 LocalTransform 수정: Ghost sync가 매 프레임 덮어써서 flickering
- PostTransformMatrix 시도: Dying 엔티티에 PostTransformMatrix 미추가 버그로 실패
- **최종 해결**: PostTransformMatrix 기반 pitch 적용 (Ghost sync와 완전 독립, gimbal lock 없음)

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 1: Dying 상태 덮어쓰기 방지 | 완료 | 계획 5개 + 추가 5개(HandleAttackRequest, HandleBuildMoveRequest, UnifiedTargeting, ResourceNodeCleanup, SoundEventEmit) 총 10개 시스템 |
| Task 2: GameSettings DeathTiltAngle 추가 | 완료 | DeathTiltAngle + CombatTiltSwingRatio 추가, Range/Min 검증 포함 |
| Task 3: CombatTiltTimer 사망 필드 추가 | 완료 | DeathTiltElapsed, WasDying 추가. SavedYaw 불필요 (PostTransformMatrix 방식으로 gimbal lock 원천 해소) |
| Task 4: EntityTiltSystem 리네임 + 확장 | 완료 | TiltUtility 공용 클래스 분리, ComponentLookup<CombatStats>로 Worker 지원, [BurstCompile] 추가 |
| Task 5: ClientDeathSystem 수정 | 완료 | WithNone(EnemyState, UnitActionState) 추가 |
| 추가: Worker 사망 기울임 수정 | 완료 | UnitActionState 쿼리에서 CombatStats 의존 제거, ComponentLookup으로 optional access |

### 변경된 파일
- `Assets/Scripts/Client/Systems/Animation/EntityTiltSystem.cs` - CombatTiltSystem → EntityTiltSystem 리네임 + TiltUtility 분리 + Dying 기울임 + ComponentLookup<CombatStats>
- `Assets/Scripts/Client/Component/Animation/CombatTiltTimer.cs` - DeathTiltElapsed, WasDying 필드 추가
- `Assets/Scripts/Client/Systems/Combat/ClientDeathSystem.cs` - WithNone(EnemyState, UnitActionState) 추가
- `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` - combatStatsLookup.Update 추가 (ECB Playback 후 갱신)
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` - CombatTiltSwingRatio, DeathTiltAngle 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` - combatTiltSwingRatio, deathTiltAngle 인스펙터 필드 + Baker 매핑
- `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` - 적/유닛 타겟팅 3개 메서드 Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Commands/Combat/HandleAttackRequestSystem.cs` - UnitActionState Lookup + Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Commands/Construction/HandleBuildMoveRequestSystem.cs` - UnitActionState Lookup + Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Commands/Gathering/HandleGatherRequestSystem.cs` - Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Commands/Gathering/HandleReturnResourceRequestSystem.cs` - Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` - Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Movement/MovementArrivalSystem.cs` - Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Gathering/WorkerGatheringSystem.cs` - 6개 지점 Dying/Dead 가드
- `Assets/Scripts/Server/Systems/Gathering/ResourceNodeCleanupSystem.cs` - SetIdleState Dying/Dead 가드

### 발견된 이슈
- Worker 엔티티 사망 기울임 미적용: attackPower=0 → CombatStats 미베이킹 → EntityTiltSystem 쿼리 불일치 → ComponentLookup으로 해결
- EntityTiltSystem struct [BurstCompile] 누락 → 코드 리뷰에서 발견, 즉시 수정

### Phase 2 완료 판정: Pass
