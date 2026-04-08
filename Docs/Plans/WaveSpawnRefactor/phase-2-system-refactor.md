# Phase 2: 시스템 리팩토링 + WavePhase 제거

## 목표

- WavePhase enum 삭제 + GamePhaseState에서 기존 호환 필드(CurrentWave, Wave0SpawnedCount) 제거
- WaveManagerSystem, EnemySpawnerSystem의 switch문을 BlobAsset 기반 데이터 주도 로직으로 교체
- GamePhaseInitSystem이 int 인덱스 기반 GamePhaseState를 초기화하도록 수정
- 기존 동작(Wave0/1/2)이 정확히 동일하게 재현되는지 검증

## 선행 조건

- Phase 1 완료 (WaveConfig, WaveSpawnSettings, GamePhaseState 변경)

## 작업 목록

> **주의**: Phase 2의 Task 0~5는 원자적으로 수행해야 한다. Task 0에서 WavePhase enum을 삭제하면 기존 시스템이 즉시 컴파일 실패하므로, 모든 Task를 한 번에 완료한 뒤 컴파일을 확인한다.

### Task 0: WavePhase enum 삭제 + GamePhaseState 정리

**수정 파일**: `Assets/Scripts/Shared/Singletons/GamePhaseState.cs`

- [ ] `WavePhase` enum 삭제
- [ ] `GamePhaseState`에서 기존 호환 필드 제거:
  - `CurrentWave: WavePhase` 제거 (Phase 1에서 추가한 `CurrentWaveIndex: int` 사용)
  - `Wave0SpawnedCount: int` 제거 (Phase 1에서 추가한 `InitialSpawnedCount: int` 사용)

### Task 1: WaveManagerSystem 리팩토링

**수정 파일**: `Assets/Scripts/Server/Systems/Wave/WaveManagerSystem.cs`

- [ ] `RequireForUpdate<GameSettings>` → `RequireForUpdate<WaveSpawnSettings>` 변경
- [ ] `RequireForUpdate<GamePhaseState>` 유지
- [ ] switch문 제거, 인덱스 기반 전환 로직:
  ```csharp
  var spawnSettings = SystemAPI.GetSingleton<WaveSpawnSettings>();
  ref var blob = ref spawnSettings.Config.Value;

  int nextIndex = phaseState.CurrentWaveIndex + 1;
  if (nextIndex < blob.Waves.Length)
  {
      ref var nextWave = ref blob.Waves[nextIndex];
      if (elapsed >= nextWave.TriggerTime || kills >= nextWave.TriggerKillCount)
      {
          phaseState.CurrentWaveIndex = nextIndex;
          phaseState.LastSpawnTime = elapsed;
          phaseState.InitialSpawnedCount = 0; // Burst 모드 전환 대비 리셋
          // 로깅
      }
  }
  ```
- [ ] 로그 메시지에 Wave 인덱스 표시 (enum 이름 대신 숫자): 기존 하드코딩 문자열 `"Wave0 -> Wave1"` 등을 `$"Wave{prev} -> Wave{next}"` 형태로 변경

### Task 2: EnemySpawnerSystem 리팩토링

**수정 파일**: `Assets/Scripts/Server/Systems/Wave/EnemySpawnerSystem.cs`

- [ ] `RequireForUpdate<GameSettings>` → `RequireForUpdate<WaveSpawnSettings>` 변경
- [ ] switch문 제거, BlobAsset 기반 분기:
  ```csharp
  var spawnSettings = SystemAPI.GetSingleton<WaveSpawnSettings>();
  ref var blob = ref spawnSettings.Config.Value;
  ref var wave = ref blob.Waves[phaseState.CurrentWaveIndex];

  if (wave.SpawnMode == SpawnMode.Burst)
      HandleBurstSpawn(ref state, ref ecb, ref phaseState, wave, ...);
  else // SpawnMode.Periodic
      HandlePeriodicSpawn(ref state, ref ecb, ref phaseState, wave, ...);
  ```
- [ ] `HandleWave0Spawn` → `HandleBurstSpawn` 범용화:
  - `settings.Wave0InitialSpawnCount` → `wave.SpawnCount`
  - `settings.Wave0SpawnSpacing` → `wave.SpawnSpacing` (Burst 모드: 그리드 간격으로 해석)
  - `wave.SpawnInterval`은 Burst 모드에서 무시 (1회성 즉시 스폰)
  - 범용적으로 `SelectEnemyPrefab(wave.SmallRate, wave.BigRate)` 사용 (현재 HandleWave0Spawn은 prefabBig 직접 사용이지만, 리팩토링 후 Burst 모드에서도 비율 기반 적 타입 결정으로 일반화)
  - 3종 프리팹 y오프셋 모두 캐싱 (HandlePeriodicSpawn과 동일, 혼합 스폰 대비)
- [ ] `HandlePeriodicSpawn` 수정:
  - 파라미터에서 GameSettings 참조 제거
  - `wave.SpawnInterval`, `wave.SpawnCount`, `wave.SpawnSpacing` 사용 (Periodic: 원형 반지름으로 해석)
  - `spawnSettings.MaxEnemyCount` 사용
  - 루프 내 `SystemAPI.TryGetSingleton<GameSettings>` 반복 호출 제거 (기존 버그 수정). BlobAsset ref를 루프 밖에서 1회만 읽고, 루프 내에서는 로컬 변수로 참조
  - **참고**: SpawnSpacing은 SpawnMode에 따라 해석이 다름 — Burst: 그리드 간격, Periodic: 원형 분산 반지름
- [ ] `SelectEnemyPrefab` 수정:
  - 파라미터: `(ref Random, Entity small, Entity big, Entity flying, float smallRate, float bigRate)`
  - 로직:
    ```csharp
    float roll = random.NextFloat(0f, 1f);
    if (roll < smallRate) return prefabSmall != Entity.Null ? prefabSmall : prefabBig;
    if (roll < smallRate + bigRate) return prefabBig;
    return prefabFlying != Entity.Null ? prefabFlying : prefabBig;
    ```
  - Flying 프리팹이 null이면 Big으로 fallback (안전장치)
- [ ] 로그 메시지 수정: `(int)phaseState.CurrentWave` → `phaseState.CurrentWaveIndex`로 변경 (253행 등 WavePhase 참조 로그 전수)

### Task 3: GamePhaseInitSystem 수정

**수정 파일**: `Assets/Scripts/Server/Systems/Initialize/GamePhaseInitSystem.cs`

- [ ] `WavePhase.Wave0` → `CurrentWaveIndex = 0`
- [ ] `Wave0SpawnedCount = 0` → `InitialSpawnedCount = 0`
- [ ] `using Shared` 유지 (WavePhase 제거에 따른 import 정리)

### Task 4: DamageApplySystem 확인

**파일**: `Assets/Scripts/Server/Systems/Combat/DamageApplySystem.cs` (66-70행)

- [ ] `phaseState.TotalKillCount += killCount` — 필드명 변경 없음, 수정 불필요 확인
- [ ] 컴파일 에러 없는지 확인

### Task 5: 테스트 파일 수정

**파일**: `Assets/Tests/PlayMode/Systems/WaveSystemTests.cs`

- [ ] WavePhase 참조 제거/수정 (주석 포함)
- [ ] GamePhaseState 새 필드명(CurrentWaveIndex, InitialSpawnedCount) 반영

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| (메인) | Task 0: WavePhase 삭제 + GamePhaseState 정리 | 먼저 실행 |
| Agent A | Task 1 + Task 3: WaveManagerSystem + GamePhaseInitSystem | Task 0 완료 후 |
| Agent B | Task 2: EnemySpawnerSystem | Task 0 완료 후 |
| (메인) | Task 4: DamageApplySystem 확인 | Agent A, B 완료 후 |

> Task 0 완료 후 Task 1+3과 Task 2는 서로 다른 파일을 수정하므로 병렬 가능.

## 테스트 요구사항

### EditMode Test

- `SelectEnemyPrefab` 비율 검증 (순수 함수이므로 EditMode 가능):
  - SmallRate=0, BigRate=1 → 항상 Big 반환
  - SmallRate=0.6, BigRate=0.4 → Small 60%, Big 40%
  - SmallRate=0.5, BigRate=0.35 → Small 50%, Big 35%, Flying 15%
  - SmallRate+BigRate > 1.0 → Flying 비율 0 (음수 안 됨)
  - prefabFlying=Entity.Null → Big fallback

### PlayMode Test

- Wave 전환 테스트:
  - ElapsedTime이 TriggerTime 도달 시 CurrentWaveIndex 증가
  - TotalKillCount가 TriggerKillCount 도달 시 CurrentWaveIndex 증가
  - 마지막 Wave 이후 추가 전환 없음
- Burst 스폰 테스트:
  - SpawnMode=0일 때 SpawnCount만큼 한번에 스폰
  - InitialSpawnedCount 추적
- Periodic 스폰 테스트:
  - SpawnInterval 간격으로 SpawnCount 스폰
  - MaxEnemyCount 도달 시 스폰 중단

## 검증 방법

1. 전체 프로젝트 컴파일 성공
2. WavePhase enum 참조 0건 (Grep 확인)
3. 서버 실행 후 기존과 동일한 Wave0→1→2 스폰 동작

## 완료 기준

- [ ] WaveManagerSystem: switch 제거, BlobAsset 기반 전환
- [ ] EnemySpawnerSystem: switch 제거, BlobAsset 기반 스폰, SelectEnemyPrefab 수정
- [ ] GamePhaseInitSystem: int 인덱스 기반 초기화
- [ ] WavePhase enum 참조 0건
- [ ] 전체 컴파일 성공
