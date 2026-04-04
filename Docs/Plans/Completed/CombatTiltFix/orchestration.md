# CombatTiltFix 오케스트레이션 플랜

## 문제 정의

1. **EnemyBig 공격 기울기 미적용**: CombatTiltSystem이 lerp 기반(`newPitch = lerp(currentPitch, targetPitch, ...)`)으로 동작하는데, Ghost 동기화가 매 프레임 LocalTransform.Rotation을 서버 값(yaw-only, pitch=0)으로 덮어쓰면 pitch가 축적되지 않음. 결과적으로 `lerp(0, 0.3, 0.128) ≈ 0.038 rad (~2도)`만 적용되어 대형 모델에서 시각적으로 인지 불가.
2. **유닛 공격 기울기 타이밍 불일치**: Attacking 상태 진입 시 고정 각도(0.3 rad)로 기울어진 채 유지됨. 개별 공격 쿨타임(AttackSpeed 기반)과 동기화된 스윙-복귀 사이클이 없어 부자연스러움.

**영향 범위**: CombatTiltSystem (Client), GameSettings/GameSettingsAuthoring (Shared/Authoring)

## AS-IS (현재 상태)

### CombatTiltSystem (`Assets/Scripts/Client/Systems/Animation/CombatTiltSystem.cs`)
- SimulationSystemGroup, ClientSimulation, UpdateAfter(VATAnimationPlaybackSystem)
- **UnitTiltJob**: `in UnitActionState, ref LocalTransform` → Attacking이면 lerp로 pitch 증가, 아니면 lerp로 0 복귀
- **EnemyTiltJob**: `in EnemyState, ref LocalTransform` → 동일 로직
- 기울기 계산: `newPitch = lerp(currentPitch, targetPitch, saturate(TiltSpeed * DeltaTime))`
  - **문제**: currentPitch가 Ghost 동기화로 매 프레임 0으로 리셋되면 축적 불가
- CombatStats(AttackSpeed) 미참조 → 공격 주기와 무관한 정적 기울기

### 서버 공격 시스템
- **MeleeAttackSystem** (FixedStepSimulationSystemGroup, Server): 범위 내 진입 → `EnemyState.CurrentState = Attacking` + `CombatUtility.RotateTowardTarget`(yaw-only rotation)
- **RangedAttackSystem**: 동일 패턴
- RotateTowardTarget이 yaw-only quaternion 생성 → Ghost 동기화 시 pitch=0

### GameSettings
- `CombatTiltAngle = 0.3f` (~17도)
- `CombatTiltSpeed = 8.0f` (lerp 속도)

### 관련 파일
| 파일 | 역할 |
|------|------|
| `Assets/Scripts/Client/Systems/Animation/CombatTiltSystem.cs` | 클라이언트 기울기 시각효과 |
| `Assets/Scripts/Server/Systems/Combat/MeleeAttackSystem.cs` | 근접 공격 + RotateTowardTarget |
| `Assets/Scripts/Server/Systems/Combat/RangedAttackSystem.cs` | 원거리 공격 + RotateTowardTarget |
| `Assets/Scripts/Shared/Utilities/CombatUtility.cs` | RotateTowardTarget (yaw-only) |
| `Assets/Scripts/Shared/Singletons/GameSettings.cs` | CombatTiltAngle, CombatTiltSpeed |
| `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | GameSettings 인스펙터 |

## TO-BE (목표 상태)

### 핵심 변경: 시간 기반 Swing-Return 사이클

이전 프레임 pitch에 의존하지 않고, **타이머에서 직접 pitch를 계산**:
1. Attacking 진입 → 타이머 리셋
2. 매 프레임: 타이머 → 공격 주기(1/AttackSpeed) 기반 사이클 계산
3. 사이클 전반부: half-sine 스윙 (0→peak→0)
4. 사이클 후반부: 대기 (pitch=0)
5. Attacking 이탈 → 현재 tilt를 감쇠하며 복귀

### 새 컴포넌트: CombatTiltTimer (클라이언트 전용)
```csharp
public struct CombatTiltTimer : IComponentData
{
    public float Timer;        // 공격 사이클 타이머
    public float CurrentTilt;  // 현재 기울기 팩터 (0~1), 복귀 시 감쇠용
    public byte WasAttacking;  // 이전 프레임 Attacking 여부
}
```
- Ghost 동기화 불필요 (클라이언트 시각효과 전용)
- CombatTiltSystem에서 ECB로 초기화 (SoundEventEmitSystem 패턴)

### CombatTiltSystem 리팩토링
- UnitTiltJob → **UnitSwingTiltJob**: `in UnitActionState, in CombatStats, ref CombatTiltTimer, ref LocalTransform`
- EnemyTiltJob → **EnemySwingTiltJob**: `in EnemyState, in CombatStats, ref CombatTiltTimer, ref LocalTransform`
- Swing 계산 (Burst 호환):
  ```
  period = 1.0 / max(attackSpeed, 0.01)
  cycleTime = fmod(timer, period)
  swingDuration = period * swingRatio

  if (cycleTime < swingDuration):
      tilt = sin(cycleTime / swingDuration * PI)  // half-sine: 0→1→0
  else:
      tilt = 0

  pitch = tiltAngle * tilt
  rotation = RotateY(currentYaw) * RotateX(pitch)  // 직접 계산, lerp 미사용
  ```

### GameSettings 추가 필드
- `CombatTiltSwingRatio` (float, default 0.4): 공격 주기 중 스윙 구간 비율 (0.4 = 40% 스윙 + 60% 대기)

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| Pitch 계산 | lerp(currentPitch, target) — 이전 프레임 의존 | timer 기반 직접 계산 — Ghost 동기화 무관 |
| 공격 주기 동기화 | 없음 (Attacking 동안 고정 각도) | AttackSpeed 기반 swing-return 사이클 |
| 스윙 형태 | 단조 증가 → 고정 유지 | half-sine 펄스 (0→peak→0) 반복 |
| 상태 추적 | 없음 (매 프레임 rotation에서 추출) | CombatTiltTimer 컴포넌트 (타이머, 이전 상태) |
| 비공격 복귀 | lerp로 서서히 감소 | 감쇠 속도로 빠르게 0 복귀 |
| EnemyBig 적용 | 미적용 (pitch 축적 불가) | 정상 적용 (timer 기반) |

## Phase 체크리스트

### Phase 1: CombatTiltTimer + Swing-Return 구현
- [x] CombatTiltTimer 컴포넌트 정의
- [x] GameSettings/GameSettingsAuthoring에 CombatTiltSwingRatio 추가
- [x] CombatTiltSystem 리팩토링 (초기화 + Swing-Return Job)
- [x] 빌드 검증
→ 상세: [phase-1-swing-return.md](./phase-1-swing-return.md)

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|-----------|
| 1 | 없음 | - |

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `Assets/Scripts/Client/Component/Animation/CombatTiltTimer.cs` | 신규 — 클라이언트 전용 타이머 컴포넌트 |
| 1 | `Assets/Scripts/Client/Systems/Animation/CombatTiltSystem.cs` | 수정 — 초기화 로직 + Swing-Return Job |
| 1 | `Assets/Scripts/Shared/Singletons/GameSettings.cs` | 수정 — CombatTiltSwingRatio 필드 추가 |
| 1 | `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | 수정 — CombatTiltSwingRatio 인스펙터 노출 |

## 검증 방법

1. EnemyBig 공격 시 기울기 시각적 확인
2. 아군 유닛 공격 시 스윙-복귀 리듬 확인 (AttackSpeed와 동기화)
3. VAT 적(EnemySmall/EnemyFlying) 기울기 정상 동작 확인 (회귀 테스트)
4. 비공격 상태 복귀 시 부드러운 감쇠 확인
5. 빌드 성공

## 롤백 전략

- Phase 1 실패 시: CombatTiltTimer 컴포넌트 삭제 + CombatTiltSystem 원복 + GameSettings 필드 제거
- 단일 Phase이므로 git revert로 일괄 롤백 가능

## 다른 계획과의 관계

- **DeathTilt 계획 (Docs/Plans/DeathTilt/)**: Phase 2에서 동일 파일을 `CombatTiltSystem.cs` → `EntityTiltSystem.cs`로 리네임하고 Dying 상태 기울임을 추가함. 실행 순서에 따라 상호 조정 필요:
  - **CombatTiltFix 먼저 실행 시**: 본 계획이 Job 구조를 변경(UnitTiltJob→UnitSwingTiltJob)하므로, DeathTilt Phase 2의 기울임 확장을 새 Job 구조에 맞게 갱신해야 함.
  - **DeathTilt 먼저 실행 시**: 본 계획의 파일 참조를 `CombatTiltSystem` → `EntityTiltSystem`으로 전부 갱신해야 함.
