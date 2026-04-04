# Phase 1: CombatTiltTimer + Swing-Return 구현

## 목표

CombatTiltSystem의 lerp 기반 정적 기울기를 시간 기반 swing-return 사이클로 교체하여:
1. EnemyBig 기울기 미적용 문제 해결 (Ghost 동기화 무관하게 동작)
2. 아군 유닛 공격 기울기 타이밍을 AttackSpeed와 동기화

## 선행 조건

- 없음

## 작업 목록

### Task 1: CombatTiltTimer 컴포넌트 정의

- [ ] `Assets/Scripts/Client/Component/Animation/CombatTiltTimer.cs` 신규 생성
  ```csharp
  public struct CombatTiltTimer : IComponentData
  {
      public float Timer;        // 공격 사이클 경과 시간
      public float CurrentTilt;  // 현재 기울기 팩터 (0~1)
      public byte WasAttacking;  // 이전 프레임 Attacking 여부
  }
  ```
- Ghost 동기화 불필요 (클라이언트 전용 시각효과)
- `byte WasAttacking`은 Burst 호환 (bool 대신)

### Task 2: GameSettings 확장

- [ ] `Assets/Scripts/Shared/Singletons/GameSettings.cs`에 필드 추가:
  ```csharp
  public float CombatTiltSwingRatio;  // 공격 주기 중 스윙 비율 (default 0.4)
  ```
- [ ] `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs`에 인스펙터 필드 추가:
  ```csharp
  [Tooltip("공격 주기 중 스윙 구간 비율 (0.4 = 40% 스윙, 60% 대기)")]
  [Range(0.1f, 0.8f)]
  public float combatTiltSwingRatio = 0.4f;
  ```
- [ ] Baker에서 `CombatTiltSwingRatio` 매핑 추가

### Task 3: CombatTiltSystem 리팩토링

- [ ] **초기화 로직 추가** (SoundEventEmitSystem 패턴 참조):
  - 유닛: `SystemAPI.Query<UnitActionState, CombatStats>().WithNone<CombatTiltTimer>()` → ECB로 CombatTiltTimer 추가
  - 적: `SystemAPI.Query<EnemyState, CombatStats>().WithNone<CombatTiltTimer>()` → ECB로 CombatTiltTimer 추가
  - CombatStats 보유 엔티티만 초기화 (Job의 Execute 파라미터와 정합)
  - OnUpdate 시작 부분에서 main thread로 실행 (structural change)

- [ ] **UnitTiltJob → UnitSwingTiltJob 교체**:
  ```
  Execute 파라미터: in UnitActionState, in CombatStats, ref CombatTiltTimer, ref LocalTransform

  1. isAttacking = (actionState.State == Action.Attacking)
  2. 상태 전이 감지:
     - !wasAttacking && isAttacking → timer = 0 (진입)
  3. 스윙 계산:
     if (isAttacking):
         timer += deltaTime
         period = 1.0 / max(attackSpeed, 0.01)
         cycleTime = fmod(timer, period)
         swingDuration = period * swingRatio
         currentTilt = cycleTime < swingDuration
             ? sin(cycleTime / swingDuration * PI)
             : 0
     else:
         currentTilt = max(0, currentTilt - returnSpeed * deltaTime)
         timer = 0
  4. wasAttacking 갱신
  5. pitch = tiltAngle * currentTilt
  6. rotation = RotateY(yaw) * RotateX(pitch)
  ```

- [ ] **EnemyTiltJob → EnemySwingTiltJob 교체**:
  - UnitSwingTiltJob과 동일 스윙 로직
  - `in EnemyState` 사용, `isAttacking = (enemyState.CurrentState == EnemyContext.Attacking)`

- [ ] **DefaultTiltAngle/DefaultTiltSpeed fallback 유지**:
  - CombatStats는 Execute 파라미터로 포함 (CombatStats 없는 엔티티는 초기화/처리 대상에서 자동 제외, 비전투 유닛은 Attacking 미진입이므로 문제 없음)
  - GameSettings 로드 실패 시 기본값 사용 (기존 패턴 유지)
  - CombatTiltSwingRatio fallback: 0.4f
  - returnSpeed는 기존 TiltSpeed 재활용 (8.0f)

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (CombatTiltTimer) + Task 2 (GameSettings) | 없음 |
| Agent B | Task 3 (CombatTiltSystem 리팩토링) | Agent A 완료 후 |

Task 1, 2는 독립 파일이므로 병렬 가능하나, Task 3이 둘 모두 참조하므로 순차가 안전.

## 테스트 요구사항

### 수동 테스트 (PlayMode)
- EnemyBig 공격 시 기울기 시각적으로 확인 (이전: 미적용 → 이후: 스윙 동작)
- 아군 근접 유닛 공격 시 스윙-복귀 리듬 확인
- 아군 원거리 유닛(Archer) 공격 시 동일 스윙 확인
- VAT 적(EnemySmall, EnemyFlying) 기울기 회귀 테스트
- 공격 중단 시(타겟 사망, 범위 이탈) 부드러운 복귀 확인
- AttackSpeed 차이가 있는 유닛 간 스윙 빈도 차이 확인

## 검증 방법

1. `/build` 빌드 성공
2. Play Mode에서 EnemyBig 기울기 동작 확인
3. Play Mode에서 아군 유닛 스윙 리듬 확인
4. 기존 VAT 적 기울기 회귀 테스트

## 완료 기준

- [x] CombatTiltTimer 컴포넌트 정의 완료
- [x] GameSettings에 CombatTiltSwingRatio 필드 추가
- [x] CombatTiltSystem swing-return 로직 구현
- [x] 빌드 성공
- [ ] EnemyBig 기울기 시각적 확인 (PlayMode 수동 테스트)
- [ ] 아군 유닛 스윙 타이밍 확인 (PlayMode 수동 테스트)
