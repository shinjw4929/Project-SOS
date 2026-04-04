# DeathTilt (사망 기울임 연출) 오케스트레이션 플랜

## 문제 정의
- 엔티티가 HP 0이 되면 Dying 상태에서 DeathDuration만큼 유지 후 파괴됨 (Phase 1 완료)
- **사망 시각 연출 없음**: `ClientDeathSystem`이 `Health <= 0` 즉시 `DisableRendering` 추가 → Dying 동안 보이지 않음
- **Dying 상태 보호 미흡**: 여러 서버 시스템이 Dying 상태를 Moving/Idle/Working으로 덮어씀
- **기울임 시스템 Dying 미처리**: `CombatTiltSystem`이 Dying 상태에서 기울임 복귀(pitch→0)만 수행

## AS-IS (현재 상태 — Phase 1 + CombatTiltFix 완료)

### 사망 흐름 (서버)
```
Frame N    : DamageApplySystem → Health = 0
Frame N    : ServerDeathSystem → DeathDetectionJob: Dying 상태 + DeathTimer 부착 (ECB)
Frame N+1~K: ServerDeathSystem → DeathTimerJob: 카운트다운
Frame K    : 타이머 만료 → DestroyEntity (ECB)
```
- `DeathTimer` 컴포넌트로 Dying 지속시간 제어 (GameSettings.DeathDuration, 기본 0.5초)
- MeleeAttackSystem/RangedAttackSystem: Dying/Dead early return 적용 완료
- PredictedMovementSystem: Dying/Dead 이동 스킵 적용 완료

### 기울임 시스템 (클라이언트)
- `CombatTiltSystem`: CombatTiltFix 완료 상태
  - Timer 기반 swing-return 사이클 (CombatTiltTimer 컴포넌트)
  - Ghost sync에서 yaw 추출 → pitch를 timer에서 직접 계산 → 합성
  - **Dying 상태 미처리**: isAttacking=false → CurrentTilt 감쇠 → pitch=0 복귀

### Dying 상태 덮어쓰기 버그
| 시스템 | 문제 | 영향 |
|---|---|---|
| HandleMoveRequestSystem | Dying 유닛에 `Action.Moving` 설정 | 죽은 유닛 이동 |
| MovementArrivalSystem | Dying 유닛을 `Action.Idle`로 설정 | Dying 상태 소실 |
| WorkerGatheringSystem | Dying 워커에 `Action.Working`/`Moving` 설정 | 죽은 워커 채집 |
| HandleGatherRequestSystem | Dying 워커에 `Action.Moving` 설정 | 죽은 워커 이동 |
| HandleReturnResourceRequestSystem | Dying 워커에 `Action.Moving` 설정 | 죽은 워커 이동 |

### 클라이언트 렌더링
- `ClientDeathSystem`: `Health <= 0` → 즉시 `DisableRendering` 추가 (유닛/적 포함)

### 관련 파일
| 파일 | 역할 |
|---|---|
| `Client/Systems/Animation/CombatTiltSystem.cs` | Timer 기반 swing-return 기울임 (CombatTiltFix 완료) |
| `Client/Component/Animation/CombatTiltTimer.cs` | Timer, CurrentTilt, WasAttacking |
| `Client/Systems/Combat/ClientDeathSystem.cs` | Health <= 0 → DisableRendering |
| `Shared/Singletons/GameSettings.cs` | CombatTiltAngle, CombatTiltSpeed, CombatTiltSwingRatio, DeathDuration |
| `Server/Systems/Combat/ServerDeathSystem.cs` | DeathDetectionJob + DeathTimerJob |
| `Server/Systems/Movement/PredictedMovementSystem.cs` | Dying/Dead 이동 스킵 (완료) |
| `Server/Systems/Combat/MeleeAttackSystem.cs` | Dying/Dead 공격 스킵 (완료) |
| `Server/Systems/Combat/RangedAttackSystem.cs` | Dying/Dead 공격 스킵 (완료) |

## TO-BE (목표 상태)

### Dying 상태 보호 (서버)
- Dying/Dead 상태를 수정하는 모든 시스템에 early return 추가
- DeathTimer가 있는 엔티티는 상태가 Dying에서 변경되지 않음

### 기울임 시스템 (클라이언트)
- `EntityTiltSystem` (CombatTiltSystem 리네임):
  - CombatTiltTimer에 `DeathTiltElapsed` + `SavedYaw` 필드 추가
  - Dying 진입 시: yaw 저장 + DeathTiltElapsed=0 초기화
  - Dying 유지 중: 저장된 yaw + 점진 pitch 적용 (timer 기반, Ghost sync 독립)
  - 기존 전투 swing-return 로직 유지

### 클라이언트 렌더링
- `ClientDeathSystem`: 유닛/적은 `DisableRendering` 추가하지 않음
- 서버 파괴 시 Ghost 자동 제거

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|---|---|---|
| Dying 상태 보호 | 공격/이동만 스킵 (5개 시스템 누락) | 전 시스템 Dying/Dead early return |
| 사망 시 기울임 | 없음 (pitch→0 복귀) | DeathTiltAngle까지 점진 기울임 |
| 사망 시 렌더링 | 즉시 숨김 | Dying 동안 표시 |
| 기울임 방식 | - | CombatTiltFix와 동일한 timer 기반 |
| yaw 처리 | - | Dying 진입 시 저장, gimbal lock 방지 |

## Phase 체크리스트

### Phase 1: 서버 사망 지속시간 도입 ✅ 완료
- [x] `DeathTimer` IComponentData 추가 (Shared)
- [x] `ServerDeathSystem` 수정: DeathDetectionJob + DeathTimerJob 분리
- [x] `GameSettings`/`GameSettingsAuthoring`에 `DeathDuration` 필드 추가
- [x] `MeleeAttackSystem`/`RangedAttackSystem`에 Dying/Dead 공격 스킵 추가
- [x] `PredictedMovementSystem`에 Dying/Dead 이동 스킵 추가
- [x] 컴파일 확인
-> 상세: [phase-1-server-death-timer.md](./phase-1-server-death-timer.md)

### Phase 2: Dying 상태 보호 + 클라이언트 사망 기울임 ✅ 완료
- [x] Dying 상태 덮어쓰기 방지 (서버 10개 시스템)
- [x] `GameSettings`/`GameSettingsAuthoring`에 `DeathTiltAngle` 필드 추가
- [x] `CombatTiltTimer`에 `DeathTiltElapsed`, `WasDying` 필드 추가
- [x] `CombatTiltSystem` → `EntityTiltSystem` 리네임 + Dying 기울임 확장
- [x] `ClientDeathSystem` 수정: 유닛/적 DisableRendering 스킵
- [x] 컴파일 확인
-> 상세: [phase-2-client-death-tilt.md](./phase-2-client-death-tilt.md)

## Phase 간 의존성
| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |
| 2 | Phase 1 (DeathTimer, DeathDuration 존재 필요) | X |

## 변경 파일 요약
| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Shared/Components/State/DeathTimer.cs` | **신규** ✅ |
| 1 | `Server/Systems/Combat/ServerDeathSystem.cs` | 2-Job 구조 ✅ |
| 1 | `Shared/Singletons/GameSettings.cs` | DeathDuration ✅ |
| 1 | `Authoring/Settings/GameSettingsAuthoring.cs` | deathDuration ✅ |
| 1 | `Server/Systems/Combat/MeleeAttackSystem.cs` | Dying/Dead 공격 스킵 ✅ |
| 1 | `Server/Systems/Combat/RangedAttackSystem.cs` | Dying/Dead 공격 스킵 ✅ |
| 1 | `Server/Systems/Movement/PredictedMovementSystem.cs` | Dying/Dead 이동 스킵 ✅ |
| 2 | `Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` | Dying/Dead early return |
| 2 | `Server/Systems/Movement/MovementArrivalSystem.cs` | Dying/Dead early return |
| 2 | `Server/Systems/Gathering/WorkerGatheringSystem.cs` | Dying/Dead early return (6개 지점) |
| 2 | `Server/Systems/Commands/Gathering/HandleGatherRequestSystem.cs` | Dying/Dead early return |
| 2 | `Server/Systems/Commands/Gathering/HandleReturnResourceRequestSystem.cs` | Dying/Dead early return |
| 2 | `Shared/Singletons/GameSettings.cs` | DeathTiltAngle 추가 |
| 2 | `Authoring/Settings/GameSettingsAuthoring.cs` | deathTiltAngle 인스펙터 추가 |
| 2 | `Client/Component/Animation/CombatTiltTimer.cs` | DeathTiltElapsed, SavedYaw 필드 추가 |
| 2 | `Client/Systems/Animation/CombatTiltSystem.cs` → `EntityTiltSystem.cs` | **리네임** + Dying 기울임 |
| 2 | `Client/Systems/Combat/ClientDeathSystem.cs` | 유닛/적 DisableRendering 스킵 |

## 검증 방법
1. Dying 중 엔티티가 이동/공격/채집하지 않는 것 확인
2. 사망 시 엔티티가 전방으로 기울어지는 시각 효과 확인
3. 기울임 중 엔티티가 화면에 보이는 것 확인
4. DeathDuration 경과 후 엔티티가 정상 파괴되는 것 확인
5. GameSettings 인스펙터에서 DeathTiltAngle 조절 시 연출 변화 확인

## 롤백 전략
- Phase 1: `DeathTimer` 삭제 + `ServerDeathSystem` 원복
- Phase 2: Early return 제거 + `EntityTiltSystem`→`CombatTiltSystem` 원복 + `ClientDeathSystem` 원복

## 다른 계획과의 관계
- **CombatTiltFix (완료)**: CombatTiltTimer + UnitSwingTiltJob/EnemySwingTiltJob 구조가 이미 적용됨. Phase 2는 이 구조 위에 Dying 분기를 추가.
