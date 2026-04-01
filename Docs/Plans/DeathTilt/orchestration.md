# DeathTilt (사망 기울임 연출) 오케스트레이션 플랜

## 문제 정의
- 엔티티가 HP 0이 되면 **1프레임 Dying 후 즉시 파괴**되어 사망 연출이 사실상 없음
- `ClientDeathSystem`이 `Health <= 0` 즉시 `DisableRendering`을 추가하여 사망 시각 효과가 보이지 않음
- `Dead(255)` 상태가 enum에 정의되어 있지만 한 번도 사용되지 않음
- 영향 범위: ServerDeathSystem, ClientDeathSystem, CombatTiltSystem, GameSettings, PredictedMovementSystem

## AS-IS (현재 상태)

### 사망 흐름 (서버)
```
Frame N  : DamageApplySystem → Health = 0
Frame N  : ServerDeathSystem → Dying 상태 설정 (ECB)
Frame N+1: ServerDeathSystem → Dying 감지 → DestroyEntity (ECB)
```
- `ServerDeathSystem` (`SimulationSystemGroup`, Server): 2단계 사망이지만 실질적으로 **1프레임 딜레이**만 존재
- `DeathTimer` 같은 지속시간 컴포넌트 없음

### 클라이언트 렌더링
- `ClientDeathSystem` (`SimulationSystemGroup`, Client): `Health <= 0` → 즉시 `DisableRendering` 추가
- 사망 시각 효과를 볼 시간이 없음

### 기울임 시스템
- `CombatTiltSystem` (`SimulationSystemGroup`, Client, `UpdateAfter(VATAnimationPlaybackSystem)`):
  - `Attacking` 상태에서만 pitch 기울임 적용
  - Dying/Dead 상태에서는 pitch → 0으로 복귀
  - `GameSettings.CombatTiltAngle` (0.3 rad ≈ 17도), `CombatTiltSpeed` (8.0) 사용

### 이동 시스템
- `PredictedMovementSystem`: `Attacking` 상태만 이동 스킵 → **Dying 상태에서도 이동 가능** (현재는 1프레임이라 무시 가능했음)

### 관련 파일
| 파일 | 역할 |
|---|---|
| `Assets/Scripts/Server/Systems/Combat/ServerDeathSystem.cs` | 2단계 사망 (Dying → Destroy) |
| `Assets/Scripts/Client/Systems/Combat/ClientDeathSystem.cs` | Health <= 0 → DisableRendering |
| `Assets/Scripts/Client/Systems/Animation/CombatTiltSystem.cs` | Attacking 상태 pitch 기울임 |
| `Assets/Scripts/Shared/Singletons/GameSettings.cs` | CombatTiltAngle, CombatTiltSpeed |
| `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | 인스펙터 노출 |
| `Assets/Scripts/Server/Systems/Movement/PredictedMovementSystem.cs` | Attacking만 이동 스킵 |
| `Assets/Scripts/Shared/Components/State/EnemyState.cs` | EnemyContext enum (Dying=254, Dead=255) |
| `Assets/Scripts/Shared/Components/State/UnitActionState.cs` | Action enum (Dying=254, Dead=255) |

## TO-BE (목표 상태)

### 사망 흐름 (서버)
```
Frame N    : DamageApplySystem → Health = 0
Frame N    : ServerDeathSystem → Dying 상태 설정 + DeathTimer 부착 (ECB)
Frame N+1~K: ServerDeathSystem → DeathTimer 카운트다운 (이동 정지)
Frame K    : 타이머 만료 → DestroyEntity (ECB)
```
- `DeathTimer` 컴포넌트로 Dying 지속시간 제어
- `GameSettings.DeathDuration`으로 인스펙터에서 조절

### 클라이언트 렌더링
- `ClientDeathSystem`: Dying 상태인 엔티티는 `DisableRendering` 추가하지 않음
- 서버가 엔티티를 파괴하면 Ghost가 자동 제거되어 클라이언트에서도 사라짐

### 기울임 시스템
- `CombatTiltSystem` 확장:
  - Dying 상태 → `DeathTiltAngle`까지 pitch 증가 (쓰러지는 연출)
  - `GameSettings.DeathTiltAngle`, `DeathTiltSpeed`로 조절

### 이동 시스템
- `PredictedMovementSystem`: Dying/Dead 상태도 이동 스킵에 포함

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|---|---|---|
| Dying 지속시간 | 1프레임 (고정) | `DeathDuration`초 (GameSettings) |
| Dead 상태 사용 | 미사용 | 미사용 (Dying → Destroy) |
| 사망 시 기울임 | 없음 | DeathTiltAngle까지 점진 기울임 |
| 사망 시 렌더링 | 즉시 숨김 | Dying 동안 표시, 파괴 시 자동 제거 |
| Dying 중 이동 | 이동 가능 (1프레임이라 무시) | 이동 정지 |
| 새 컴포넌트 | - | `DeathTimer` (Shared, 비동기화) |
| GameSettings 필드 | CombatTiltAngle, CombatTiltSpeed | + DeathDuration, DeathTiltAngle, DeathTiltSpeed |

## Phase 체크리스트

### Phase 1: 서버 사망 지속시간 도입
- [ ] `DeathTimer` IComponentData 추가 (Shared)
- [ ] `ServerDeathSystem` 수정: DeathDetectionJob + DeathTimerJob 분리
- [ ] `GameSettings`/`GameSettingsAuthoring`에 `DeathDuration` 필드 추가
- [ ] `PredictedMovementSystem`에 Dying/Dead 이동 스킵 추가
- [ ] 컴파일 확인
-> 상세: [phase-1-server-death-timer.md](./phase-1-server-death-timer.md)

### Phase 2: 클라이언트 사망 기울임 연출
- [ ] `GameSettings`/`GameSettingsAuthoring`에 `DeathTiltAngle`, `DeathTiltSpeed` 필드 추가
- [ ] `CombatTiltSystem` 확장: Dying 상태 사망 기울임
- [ ] `ClientDeathSystem` 수정: Dying 엔티티 DisableRendering 스킵
- [ ] 컴파일 확인
-> 상세: [phase-2-client-death-tilt.md](./phase-2-client-death-tilt.md)

## Phase 간 의존성
| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |
| 2 | Phase 1 (DeathTimer, DeathDuration 존재 필요) | X |

## 변경 파일 요약
| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Shared/Components/State/DeathTimer.cs` | **신규** - DeathTimer IComponentData |
| 1 | `Server/Systems/Combat/ServerDeathSystem.cs` | 2-Job 구조로 변경 (DeathDetectionJob + DeathTimerJob) |
| 1 | `Shared/Singletons/GameSettings.cs` | DeathDuration 필드 추가 |
| 1 | `Authoring/Settings/GameSettingsAuthoring.cs` | deathDuration 인스펙터 필드 추가 |
| 1 | `Server/Systems/Movement/PredictedMovementSystem.cs` | Dying/Dead 이동 스킵 |
| 2 | `Shared/Singletons/GameSettings.cs` | DeathTiltAngle, DeathTiltSpeed 필드 추가 |
| 2 | `Authoring/Settings/GameSettingsAuthoring.cs` | deathTiltAngle, deathTiltSpeed 인스펙터 필드 추가 |
| 2 | `Client/Systems/Animation/CombatTiltSystem.cs` | Dying 상태 기울임 로직 추가 |
| 2 | `Client/Systems/Combat/ClientDeathSystem.cs` | Dying 엔티티 DisableRendering 스킵 |

## 검증 방법
1. 적/유닛 사망 시 `DeathDuration`초 동안 Dying 상태 유지 확인 (서버 로그 또는 Entity Debugger)
2. 사망 시 엔티티가 전방으로 기울어지는 시각 효과 확인
3. Dying 중 엔티티가 이동하지 않는 것 확인
4. 기울임 완료 후 엔티티가 정상 파괴되는 것 확인
5. GameSettings 인스펙터에서 DeathDuration, DeathTiltAngle, DeathTiltSpeed 조절 가능 확인

## 롤백 전략
- Phase 1: `DeathTimer` 컴포넌트 삭제 + `ServerDeathSystem`을 기존 2단계 로직으로 복원
- Phase 2: `CombatTiltSystem`, `ClientDeathSystem` 변경 revert (Phase 1은 유지 가능)
