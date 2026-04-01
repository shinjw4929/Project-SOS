# Phase 1: 서버 사망 지속시간 도입

## 목표
- 엔티티가 Dying 상태에서 일정 시간 동안 유지된 후 파괴되도록 변경
- 사망 지속시간을 GameSettings에서 조절 가능하게 함
- Dying 중 이동을 차단하여 기울임 연출과 충돌하지 않도록 함

## 선행 조건
- 없음 (첫 번째 Phase)

## 작업 목록

### Task 1: DeathTimer 컴포넌트 생성

- [ ] `Assets/Scripts/Shared/Components/State/DeathTimer.cs` 신규 생성
  ```csharp
  public struct DeathTimer : IComponentData
  {
      public float Remaining;
  }
  ```
- Ghost 동기화 불필요 (서버에서만 타이머 관리, 클라이언트는 Dying GhostField로 기울임 시작)
- Burst 호환 struct (managed 타입 없음)

### Task 2: GameSettings에 DeathDuration 추가

- [ ] `Shared/Singletons/GameSettings.cs` — 애니메이션 섹션에 필드 추가:
  ```csharp
  /// <summary>사망 연출 지속시간 (초)</summary>
  public float DeathDuration;
  ```
- [ ] `Authoring/Settings/GameSettingsAuthoring.cs` — Animation 헤더 아래에 추가:
  ```csharp
  [Tooltip("사망 연출 지속시간 (초)")]
  [Min(0.1f)]
  public float deathDuration = 0.5f;
  ```
- [ ] Baker 매핑에 `DeathDuration = authoring.deathDuration` 추가

### Task 3: ServerDeathSystem 리팩토링

현재 단일 `ServerDeathJob`을 **2개 Job**으로 분리:

- [ ] **DeathDetectionJob** (`[WithNone(typeof(DeathTimer))]`):
  - Query: `Entity`, `ref Health` — DeathTimer가 **없는** 엔티티만
  - 기존 ComponentLookup 유지: `UnitActionState`, `EnemyState`, `ProductionCost`, `GhostOwner` (ReadOnly) + `NetworkIdToEconomyEntity` (NativeParallelHashMap)
  - Health <= 0 감지 → ECB로 Dying 상태 설정 + `DeathTimer { Remaining = DeathDuration }` 추가
  - 유닛: `ReturnPopulation` 호출 (기존 로직 유지)
  - 적: Dying 상태만 설정
  - 기타 (건물 등): 즉시 DestroyEntity (기존 로직 유지, DeathTimer 부착 안 함)

- [ ] **DeathTimerJob**:
  - Query: `Entity`, `ref DeathTimer` (Health 불필요 — DeathTimer는 Health <= 0인 엔티티에만 부착됨)
  - `DeathTimer.Remaining -= DeltaTime`
  - Remaining <= 0 → ECB로 DestroyEntity

- [ ] OnUpdate에서 두 Job을 순차 스케줄 (DeathDetectionJob → DeathTimerJob)
  - DeathDetectionJob이 ECB로 DeathTimer를 추가하므로, 같은 프레임에서 DeathTimerJob이 처리하지 않음 (ECB는 프레임 끝 재생)
  - 따라서 최소 1프레임 후부터 타이머 카운트다운 시작

- [ ] `DeathDuration` 값 전달: `SystemAPI.TryGetSingleton<GameSettings>()` → fallback 0.5f

### Task 4: PredictedMovementSystem Dying/Dead 이동 스킵

- [ ] `Server/Systems/Movement/PredictedMovementSystem.cs` 수정
- 기존 `isEnemyAttacking`/`isUnitAttacking` 조건을 확장하여 Dying/Dead 상태도 이동 스킵에 포함:
  ```csharp
  // 기존 Attacking 체크를 Dying/Dead까지 확장 (변수명도 의미에 맞게 변경)
  bool isEnemyInactive = EnemyTagLookup.HasComponent(entity) &&
                         EnemyStateLookup.TryGetComponent(entity, out EnemyState enemyState) &&
                         (enemyState.CurrentState == EnemyContext.Attacking ||
                          enemyState.CurrentState == EnemyContext.Dying ||
                          enemyState.CurrentState == EnemyContext.Dead);
  bool isUnitInactive = ActionStateLookup.TryGetComponent(entity, out UnitActionState actionState) &&
                        (actionState.State == Action.Attacking ||
                         actionState.State == Action.Dying ||
                         actionState.State == Action.Dead);
  bool skipMovement = isEnemyInactive || isUnitInactive || isWaypointsDisabled || isPathPending;
  ```
- 기존 변수명 `isEnemyAttacking`/`isUnitAttacking`을 `isEnemyInactive`/`isUnitInactive`로 변경하여 의미 확장

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Agent A | Task 1 (DeathTimer) + Task 2 (GameSettings) | 없음 |
| Agent B | Task 3 (ServerDeathSystem) | Agent A 완료 후 (DeathTimer 타입 참조) |
| 메인 | Task 4 (PredictedMovementSystem) | Agent B와 병렬 가능 (다른 파일) |

## 테스트 요구사항

### EditMode Test
- `DeathTimer` 구조체 생성 및 Remaining 필드 설정 확인

### PlayMode Test (필요 시)
- 적 엔티티 Health를 0으로 설정 → N프레임 후 Dying 상태 유지 확인
- DeathDuration 경과 후 엔티티 파괴 확인

## 검증 방법
- 컴파일 성공
- Entity Debugger에서 Dying 엔티티에 DeathTimer 컴포넌트가 부착되고 카운트다운되는 것 확인
- Dying 엔티티가 이동하지 않는 것 확인

## 완료 기준
- [ ] DeathTimer 컴포넌트 존재
- [ ] ServerDeathSystem이 DeathDuration만큼 Dying 상태 유지 후 파괴
- [ ] GameSettings 인스펙터에서 DeathDuration 조절 가능
- [ ] Dying 엔티티 이동 정지
- [ ] 컴파일 성공
