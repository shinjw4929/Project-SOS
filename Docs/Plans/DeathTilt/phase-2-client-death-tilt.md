# Phase 2: 클라이언트 사망 기울임 연출

## 목표
- Dying 상태의 엔티티에 전방 기울임(pitch) 애니메이션을 적용하여 쓰러지는 시각 효과 구현
- 사망 기울임 각도/속도를 GameSettings에서 조절 가능하게 함
- Dying 엔티티가 기울임 중 보이도록 ClientDeathSystem 수정

## 선행 조건
- Phase 1 완료 (DeathTimer, DeathDuration, Dying 지속시간 확보)

## 작업 목록

### Task 1: GameSettings에 사망 기울임 필드 추가

- [ ] `Shared/Singletons/GameSettings.cs` — 애니메이션 섹션에 추가:
  ```csharp
  /// <summary>사망 기울임 목표 각도 (라디안, 기본 1.57 ≈ 90도)</summary>
  public float DeathTiltAngle;
  /// <summary>사망 기울임 보간 속도 (높을수록 빠름)</summary>
  public float DeathTiltSpeed;
  ```
- [ ] `Authoring/Settings/GameSettingsAuthoring.cs` — Animation 헤더 아래에 추가:
  ```csharp
  [Tooltip("사망 기울임 목표 각도 (라디안, 1.57 ≈ 90도)")]
  [Min(0f)]
  public float deathTiltAngle = 1.57f;
  [Tooltip("사망 기울임 보간 속도 (높을수록 빠름)")]
  [Min(0.1f)]
  public float deathTiltSpeed = 5.0f;
  ```
- [ ] Baker 매핑에 `DeathTiltAngle = authoring.deathTiltAngle`, `DeathTiltSpeed = authoring.deathTiltSpeed` 추가

### Task 2: CombatTiltSystem 확장

- [ ] `Client/Systems/Animation/CombatTiltSystem.cs` 수정
- OnUpdate에서 `DeathTiltAngle`, `DeathTiltSpeed` 값을 GameSettings에서 읽어 Job에 전달
- **UnitTiltJob 수정**:
  ```csharp
  public float DeathTiltAngle;
  public float DeathTiltSpeed;

  void Execute(in UnitActionState actionState, ref LocalTransform transform)
  {
      bool isAttacking = actionState.State == Action.Attacking;
      bool isDying = actionState.State == Action.Dying;

      float targetPitch, speed;
      if (isDying)
      {
          targetPitch = DeathTiltAngle;
          speed = DeathTiltSpeed;
      }
      else
      {
          targetPitch = isAttacking ? TiltAngle : 0f;
          speed = TiltSpeed;
      }

      ApplyTilt(ref transform, targetPitch, speed);
  }
  ```
- **EnemyTiltJob 수정**: 동일 패턴 (`EnemyContext.Dying` 체크)
- **ApplyTilt 시그니처 변경**: `ApplyTilt(ref LocalTransform, float targetPitch, float speed)` — speed를 파라미터로 받도록

### Task 3: ClientDeathSystem 수정

- [ ] `Client/Systems/Combat/ClientDeathSystem.cs` 수정
- Dying 상태인 엔티티는 `DisableRendering`을 추가하지 않도록 변경
- 두 가지 방법 중 선택:
  - **방법 A (권장)**: 쿼리에 `[WithNone(typeof(EnemyState), typeof(UnitActionState))]` 추가 → 유닛/적이 아닌 엔티티(건물 등)만 즉시 숨김. 유닛/적은 서버 파괴 시 Ghost 제거로 자동 사라짐
  - **방법 B**: Job 내에서 EnemyState/UnitActionState Lookup으로 Dying 여부 확인 → Dying이면 스킵
- 방법 A가 더 간결하고 Lookup 비용 없음. 단, 유닛/적 이외의 Health 보유 엔티티(건물)는 기존대로 즉시 숨김 유지

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Agent A | Task 1 (GameSettings) + Task 2 (CombatTiltSystem) | 없음 |
| Agent B | Task 3 (ClientDeathSystem) | 없음 (Agent A와 병렬 가능, 다른 파일) |

## 테스트 요구사항

### EditMode Test
- ApplyTilt 메서드: targetPitch=1.57, 여러 프레임 호출 시 pitch가 목표값에 수렴하는지 확인

### PlayMode Test (필요 시)
- 적 엔티티 Dying 상태 설정 → CombatTiltSystem 실행 후 Rotation pitch가 증가하는지 확인
- Dying 엔티티에 DisableRendering이 추가되지 않는 것 확인

## 검증 방법
- 컴파일 성공
- 적/유닛 사망 시 전방으로 쓰러지는 기울임 시각 효과 확인
- 기울임 중 엔티티가 화면에 보이는 것 확인
- GameSettings 인스펙터에서 DeathTiltAngle, DeathTiltSpeed 조절 시 연출 변화 확인

## 완료 기준
- [ ] Dying 상태 엔티티에 DeathTiltAngle까지 점진 기울임 적용
- [ ] Dying 엔티티가 기울임 중 화면에 표시됨
- [ ] GameSettings에서 DeathTiltAngle, DeathTiltSpeed 조절 가능
- [ ] 건물 등 비-유닛/적 엔티티는 기존대로 즉시 DisableRendering
- [ ] 컴파일 성공
