# Phase 2: Dying 상태 보호 + 클라이언트 사망 기울임

## 목표
- Dying 상태를 덮어쓰는 서버 시스템 버그 수정 (5개 시스템)
- CombatTiltFix의 timer 기반 패턴을 활용한 사망 기울임 구현
- Dying 엔티티가 기울임 중 보이도록 ClientDeathSystem 수정
- CombatTiltSystem → EntityTiltSystem 리네임

## 선행 조건
- Phase 1 완료 (DeathTimer, DeathDuration, Dying 지속시간 확보)
- CombatTiltFix 완료 (CombatTiltTimer, UnitSwingTiltJob/EnemySwingTiltJob 구조)

## 작업 목록

### Task 1: Dying 상태 덮어쓰기 방지 (서버)

UnitActionState를 수정하는 모든 서버 시스템에 Dying/Dead early return 추가.

- [ ] `Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` (라인 141)
  - `_unitActionStateLookup.GetRefRW(unitEntity).ValueRW.State = Action.Moving` 앞에:
  ```csharp
  var actionRef = _unitActionStateLookup.GetRefRW(unitEntity);
  if (actionRef.ValueRO.State == Action.Dying || actionRef.ValueRO.State == Action.Dead)
      return;
  actionRef.ValueRW.State = Action.Moving;
  ```
- [ ] `Server/Systems/Movement/MovementArrivalSystem.cs` (라인 65)
  - `actionState.ValueRW.State = Action.Idle` 앞에:
  ```csharp
  if (actionState.ValueRO.State == Action.Dying || actionState.ValueRO.State == Action.Dead)
      continue;
  ```
- [ ] `Server/Systems/Gathering/WorkerGatheringSystem.cs` (6개 지점: 라인 194, 305, 411, 422, 482, 568)
  - 각 `actionState.State` / `actionState.ValueRW.State` 수정 전에 Dying/Dead 가드:
  ```csharp
  if (actionState.ValueRO.State == Action.Dying || actionState.ValueRO.State == Action.Dead)
      return;  // 또는 continue (루프/쿼리 내부)
  ```
- [ ] `Server/Systems/Commands/Gathering/HandleGatherRequestSystem.cs` (라인 181)
  - `actionRW.ValueRW.State = Action.Moving` 앞에 Dying/Dead 체크
- [ ] `Server/Systems/Commands/Gathering/HandleReturnResourceRequestSystem.cs` (라인 163)
  - `actionRW.ValueRW.State = Action.Moving` 앞에 Dying/Dead 체크

### Task 2: GameSettings에 DeathTiltAngle 추가

- [ ] `Shared/Singletons/GameSettings.cs` — 사망 연출 섹션에 추가:
  ```csharp
  /// <summary>사망 기울임 목표 각도 (라디안, 기본 1.05 ≈ 60도)</summary>
  public float DeathTiltAngle;
  ```
- [ ] `Authoring/Settings/GameSettingsAuthoring.cs` — Death 헤더 아래에 추가:
  ```csharp
  [Tooltip("사망 기울임 목표 각도 (라디안, 1.05 ≈ 60도)")]
  [Range(0.3f, 1.3f)]
  public float deathTiltAngle = 1.05f;
  ```
- [ ] Baker 매핑에 `DeathTiltAngle = authoring.deathTiltAngle` 추가
- **참고**: 90도(1.57)가 아닌 60도(1.05) 기본값 사용 — 90도에서 yaw 추출 시 gimbal lock 발생

### Task 3: CombatTiltTimer에 사망 기울임 필드 추가

- [ ] `Client/Component/Animation/CombatTiltTimer.cs` 수정:
  ```csharp
  public struct CombatTiltTimer : IComponentData
  {
      public float Timer;           // 공격 사이클 타이머
      public float CurrentTilt;     // 현재 기울기 팩터 (0~1)
      public byte WasAttacking;     // 이전 프레임 Attacking 여부
      public float DeathTiltElapsed; // 사망 기울임 경과 시간
      public float SavedYaw;        // Dying 진입 시 저장한 yaw (gimbal lock 방지)
      public byte WasDying;         // 이전 프레임 Dying 여부
  }
  ```
- Ghost 동기화 불필요 (클라이언트 전용)
- 기존 필드 유지, 사망 관련 필드 3개 추가

### Task 4: CombatTiltSystem → EntityTiltSystem 리네임 + Dying 기울임 확장

- [ ] `Client/Systems/Animation/CombatTiltSystem.cs` → `EntityTiltSystem.cs` 파일명 변경
- [ ] 클래스명 `CombatTiltSystem` → `EntityTiltSystem` 변경
- [ ] OnUpdate에서 `DeathTiltAngle`, `DeathDuration` 값을 GameSettings에서 읽어 Job에 전달
- [ ] **UnitSwingTiltJob 수정**:
  ```csharp
  public float DeathTiltAngle;
  public float DeathDuration;

  void Execute(in UnitActionState actionState, in CombatStats combatStats,
      ref CombatTiltTimer tiltTimer, ref LocalTransform transform)
  {
      bool isAttacking = actionState.State == Action.Attacking;
      bool isDying = actionState.State == Action.Dying;

      if (isDying)
      {
          ComputeDeathTilt(ref tiltTimer, ref transform);
          return;
      }

      float attackSpeed = combatStats.AttackSpeed;
      ComputeSwingTilt(ref tiltTimer, ref transform, isAttacking, attackSpeed);
  }
  ```
- [ ] **ComputeDeathTilt 메서드** (UnitSwingTiltJob/EnemySwingTiltJob 공통):
  ```csharp
  private void ComputeDeathTilt(ref CombatTiltTimer timer, ref LocalTransform transform)
  {
      if (timer.WasDying == 0)
      {
          // Dying 진입: 현재 yaw 저장
          float3 fwd = math.mul(transform.Rotation, math.forward());
          timer.SavedYaw = math.atan2(fwd.x, fwd.z);
          timer.DeathTiltElapsed = 0f;
          timer.CurrentTilt = 0f; // 전투 기울임 리셋
      }

      timer.DeathTiltElapsed += DeltaTime;
      timer.WasDying = 1;

      // DeathDuration 동안 0 → DeathTiltAngle 선형 보간
      float progress = math.saturate(timer.DeathTiltElapsed / math.max(DeathDuration, 0.1f));
      float pitch = DeathTiltAngle * progress;

      // 저장된 yaw 사용 (Ghost sync 영향 없음, gimbal lock 방지)
      transform.Rotation = math.mul(
          quaternion.RotateY(timer.SavedYaw),
          quaternion.RotateX(pitch));
  }
  ```
- [ ] **EnemySwingTiltJob**: 동일 패턴 (`EnemyContext.Dying` 체크)
- [ ] **ComputeSwingTilt 수정**: Dying 상태 진입 시 WasDying 리셋:
  ```csharp
  // ComputeSwingTilt 끝에 추가
  timer.WasDying = 0;
  ```

### Task 5: ClientDeathSystem 수정

- [ ] `Client/Systems/Combat/ClientDeathSystem.cs` 수정
- ClientDeathJob에 `[WithNone(typeof(EnemyState), typeof(UnitActionState))]` 추가
- 유닛/적은 DisableRendering 스킵 → 서버 파괴 시 Ghost 자동 제거로 사라짐
- 건물 등 비유닛/비적 엔티티는 기존대로 즉시 DisableRendering

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Agent A | Task 1 (Dying 상태 보호 — 5개 서버 시스템) | 없음 |
| Agent B | Task 2 (GameSettings) + Task 3 (CombatTiltTimer 확장) | 없음 |
| 메인 | Task 4 (EntityTiltSystem) + Task 5 (ClientDeathSystem) | Agent B 완료 후 |

## 검증 방법
- 컴파일 성공
- Dying 중 엔티티가 이동/공격/채집하지 않는 것 확인
- 적/유닛 사망 시 전방으로 기울어지는 시각 효과 확인 (flickering 없음)
- 기울임 중 엔티티가 화면에 보이는 것 확인
- 공격 기울임(swing-return)이 기존과 동일하게 동작 (회귀 테스트)

## 완료 기준
- [x] Dying 상태가 다른 시스템에 의해 덮어씌워지지 않음 (10개 시스템 수정)
- [x] CombatTiltSystem.cs → EntityTiltSystem.cs 리네임 완료
- [x] Dying 상태 엔티티에 DeathTiltAngle까지 점진 기울임 적용 (flickering 없음)
- [x] Dying 엔티티가 기울임 중 화면에 표시됨
- [x] GameSettings에서 DeathTiltAngle 조절 가능
- [x] 기존 전투 기울임 동작 유지 (회귀)
- [x] 컴파일 성공
