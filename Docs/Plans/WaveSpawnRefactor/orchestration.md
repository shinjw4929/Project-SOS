# Wave 스폰 시스템 데이터 주도 리팩토링 오케스트레이션 플랜

## 문제 정의

- 현재 Wave 시스템은 `WavePhase` enum 3개(Wave0/1/2)에 하드코딩되어 있어, Wave 추가 시 enum + switch문 + GameSettings 필드를 모두 수정해야 함
- 10개 이상 Wave 확장 예정이므로 현재 구조로는 유지보수 불가
- Wave/스폰 관련 설정 14개가 GameSettings에 flat하게 흩어져 있어 밸런싱 조절이 어려움
- 적 비율 설정의 네이밍이 실제 동작과 불일치 (`EnemyBigSpawnRate`가 Flying 경계값으로 사용), Wave2 Small 50%가 하드코딩
- 영향 범위: `WaveManagerSystem`, `EnemySpawnerSystem`, `GamePhaseInitSystem`, `GamePhaseState`, `GameSettings`, `GameSettingsAuthoring`, `DamageApplySystem`(TotalKillCount 참조), 관련 문서 3건

## AS-IS (현재 상태)

### 데이터 구조

```
WavePhase enum (byte): Wave0=0, Wave1=1, Wave2=2  (3개 고정)

GamePhaseState (IComponentData, 서버 전용 싱글톤, Ghost 미동기화):
  - CurrentWave: WavePhase
  - ElapsedTime: float
  - TotalKillCount: int
  - Wave0SpawnedCount: int
  - LastSpawnTime: float

GameSettings (IComponentData 싱글톤) 내 Wave/스폰 관련 필드 14개:
  - Wave0InitialSpawnCount, Wave1TriggerTime, Wave1TriggerKillCount
  - Wave2TriggerTime, Wave2TriggerKillCount
  - Wave1SpawnInterval, Wave1SpawnCount, Wave2SpawnInterval, Wave2SpawnCount
  - MaxEnemyCount, EnemyBigSpawnRate, EnemySmallOnlyRate
  - Wave0SpawnSpacing, PeriodicSpawnSpacing
```

### 시스템 흐름

```
[InitializationSystemGroup]
  GamePhaseInitSystem (1회) → GamePhaseState 싱글톤 생성 (CurrentWave=Wave0)

[SimulationSystemGroup]
  WaveManagerSystem:
    - ElapsedTime += deltaTime
    - switch(CurrentWave) → Wave0/1/2 전환 조건 체크 (시간 OR 킬 수)

  EnemySpawnerSystem (UpdateAfter WaveManagerSystem):
    - switch(CurrentWave) → Wave0: HandleWave0Spawn / Wave1,2: HandlePeriodicSpawn
    - SelectEnemyPrefab(): 하드코딩된 0.50f + EnemyBigSpawnRate로 비율 결정

[FixedStepSimulationSystemGroup]
  DamageApplySystem → GamePhaseState.TotalKillCount += killCount
```

### 관련 파일 목록

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/Shared/Singletons/GamePhaseState.cs` | WavePhase enum + GamePhaseState 정의 |
| `Assets/Scripts/Shared/Singletons/GameSettings.cs` | Wave/스폰 설정 필드 14개 |
| `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | Inspector 설정 + Baker |
| `Assets/Scripts/Server/Systems/Wave/WaveManagerSystem.cs` | Wave 전환 로직 (switch) |
| `Assets/Scripts/Server/Systems/Wave/EnemySpawnerSystem.cs` | 적 스폰 로직 (switch + SelectEnemyPrefab) |
| `Assets/Scripts/Server/Systems/Initialize/GamePhaseInitSystem.cs` | GamePhaseState 초기화 |
| `Assets/Scripts/Server/Systems/Combat/DamageApplySystem.cs` | TotalKillCount 갱신 (66-70행) |
| `Assets/Scripts/Shared/Singletons/Ref/EnemyPrefabCatalog.cs` | 적 프리팹 참조 (SmallPrefab, BigPrefab, FlyingPrefab) |
| `Docs/Architecture/game-rules.md` | Wave System 규칙 문서 |
| `Docs/Systems/Project-SOS 상태 시스템 설계.md` | GamePhaseState/WavePhase 문서 |
| `Docs/Systems/시스템 그룹 및 의존성.md` | 시스템 배치 문서 |

## TO-BE (목표 상태)

### 데이터 구조

```
WavePhase enum → Phase 2에서 삭제 (Phase 1에서는 유지하여 컴파일 보장)

SpawnMode enum (byte): Burst=0, Periodic=1  (Shared 어셈블리에 정의)

WaveConfig (blittable struct, BlobArray 원소):
  - TriggerTime: float          // 이 Wave 활성화 경과 시간 (Wave0=0)
  - TriggerKillCount: int       // 이 Wave 활성화 처치 수 (Wave0=0)
  - SpawnMode: SpawnMode        // Burst(한번에) / Periodic(주기적)
  - SpawnCount: int             // Burst: 총 수, Periodic: 1회 수
  - SpawnInterval: float        // Periodic 주기 (초), Burst면 무시
  - SpawnSpacing: float         // 배치 간격 (m)
  - SmallRate: float            // EnemySmall 비율 (0~1)
  - BigRate: float              // EnemyBig 비율 (0~1, Flying = 1 - Small - Big)

WaveConfigBlob (BlobAsset):
  - Waves: BlobArray<WaveConfig>

WaveSpawnSettings (IComponentData 싱글톤):
  - Config: BlobAssetReference<WaveConfigBlob>
  - MaxEnemyCount: int

GamePhaseState (변경):
  - CurrentWaveIndex: int       // WavePhase → int
  - ElapsedTime: float          // 유지
  - TotalKillCount: int         // 유지
  - InitialSpawnedCount: int    // Wave0SpawnedCount → 범용화 (Burst 모드 공통)
  - LastSpawnTime: float        // 유지

GameSettings에서 Wave/스폰 관련 필드 14개 제거
```

### 시스템 흐름

```
[InitializationSystemGroup]
  GamePhaseInitSystem (1회) → GamePhaseState 싱글톤 생성 (CurrentWaveIndex=0)

[SimulationSystemGroup]
  WaveManagerSystem:
    - ElapsedTime += deltaTime
    - nextIndex = CurrentWaveIndex + 1
    - if (nextIndex < blob.Waves.Length):
        nextWave = blob.Waves[nextIndex]
        if (elapsed >= nextWave.TriggerTime OR kills >= nextWave.TriggerKillCount):
          CurrentWaveIndex = nextIndex, LastSpawnTime 리셋, InitialSpawnedCount = 0

  EnemySpawnerSystem (UpdateAfter WaveManagerSystem):
    - wave = blob.Waves[CurrentWaveIndex]
    - if (wave.SpawnMode == Burst): HandleBurstSpawn(wave)
    - if (wave.SpawnMode == Periodic): HandlePeriodicSpawn(wave)
    - SelectEnemyPrefab(wave.SmallRate, wave.BigRate)

[FixedStepSimulationSystemGroup]
  DamageApplySystem → 변경 없음 (GamePhaseState.TotalKillCount 그대로)
```

### Authoring (인스펙터)

```
WaveSpawnSettingsAuthoring (MonoBehaviour):
  [Header("Global")]
  - maxEnemyCount: int = 1200

  [Header("Wave Configs")]
  - waves: List<WaveEntry>
    [0] triggerTime=0,   triggerKillCount=0,  mode=Burst,    count=30, spacing=1,  smallRate=0, bigRate=1
    [1] triggerTime=60,  triggerKillCount=15, mode=Periodic, count=3,  interval=5, spacing=3, smallRate=0.6, bigRate=0.4
    [2] triggerTime=120, triggerKillCount=30, mode=Periodic, count=4,  interval=4, spacing=3, smallRate=0.5, bigRate=0.35
    ...Wave 9, 10... (인스펙터에서 항목 추가만 하면 됨)

  Baker → BlobAsset 생성 + WaveSpawnSettings 싱글톤 등록
```

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| Wave 수 | enum 3개 고정 | BlobArray 크기로 무제한 |
| Wave 추가 | enum/switch/GameSettings 수정 (4파일) | 인스펙터 리스트 항목 추가 (코드 수정 0) |
| 전환 로직 | switch문 분기 | 인덱스 기반 루프 |
| 스폰 로직 | switch문 분기 | SpawnMode 분기 (Burst/Periodic) |
| 적 비율 | 하드코딩 0.50f + 혼란스러운 네이밍 | SmallRate/BigRate 명시적 |
| 설정 위치 | GameSettings에 14개 필드 산재 | WaveSpawnSettingsAuthoring 한 곳 |
| 설정 구조 | flat (Wave1X, Wave2X...) | 리스트 (Wave별 구조체) |
| GameSettings 크기 | 97개 필드 (Wave/스폰 14개 포함) | 83개 필드 (Wave/스폰 제거) |

## Phase 체크리스트

### Phase 1: 데이터 구조 정의 + Authoring/Baker
- [ ] `SpawnMode` enum 정의 (Shared 어셈블리)
- [ ] `WaveConfig` struct 정의
- [ ] `WaveConfigBlob` BlobAsset 정의
- [ ] `WaveSpawnSettings` 싱글톤 정의
- [ ] `WaveSpawnSettingsAuthoring` + Baker 구현 (List<WaveEntry> → BlobAsset, 비율 합 >1.0 클램프 검증)
- [ ] `GamePhaseState`에 `CurrentWaveIndex`, `InitialSpawnedCount` 필드 추가 (기존 필드 유지)
- [ ] 컴파일 확인 (WavePhase enum은 Phase 2에서 삭제)
-> 상세: [phase-1-data-structure.md](./phase-1-data-structure.md)

### Phase 2: 시스템 리팩토링 + WavePhase 제거
- [ ] `WavePhase` enum 삭제 + `GamePhaseState`에서 기존 `CurrentWave`/`Wave0SpawnedCount` 필드 제거
- [ ] `WaveManagerSystem` 리팩토링 (switch → 인덱스 기반 루프, Wave 전환 시 InitialSpawnedCount 리셋)
- [ ] `EnemySpawnerSystem` 리팩토링 (switch → SpawnMode 분기, SelectEnemyPrefab 수정)
- [ ] `GamePhaseInitSystem` 수정 (CurrentWaveIndex=0)
- [ ] `DamageApplySystem` 변경 불필요 확인 (TotalKillCount 필드명 유지)
- [ ] `WaveSystemTests.cs` WavePhase 참조 수정
- [ ] 컴파일 확인
-> 상세: [phase-2-system-refactor.md](./phase-2-system-refactor.md)

### Phase 3: GameSettings 정리 + 문서 업데이트
- [ ] `GameSettings`에서 Wave/스폰 관련 14개 필드 제거
- [ ] `GameSettingsAuthoring`에서 대응 필드 + Baker 매핑 제거
- [ ] 문서 업데이트 (game-rules.md, 상태 시스템 설계.md, 시스템 그룹 및 의존성.md, 코드베이스 구조.md, CLAUDE.md)
- [ ] 컴파일 확인
-> 상세: [phase-3-cleanup.md](./phase-3-cleanup.md)

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|-----------|
| 1 | 없음 | - |
| 2 | Phase 1 | X |
| 3 | Phase 2 | X |

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `Assets/Scripts/Shared/Singletons/GamePhaseState.cs` | CurrentWaveIndex, InitialSpawnedCount 필드 추가 (기존 유지) |
| 1 | `Assets/Scripts/Shared/Singletons/WaveSpawnSettings.cs` | **신규** SpawnMode enum, WaveConfig, WaveConfigBlob, WaveSpawnSettings 정의 |
| 1 | `Assets/Scripts/Authoring/Settings/WaveSpawnSettingsAuthoring.cs` | **신규** WaveEntry, WaveSpawnSettingsAuthoring + Baker |
| 2 | `Assets/Scripts/Shared/Singletons/GamePhaseState.cs` | WavePhase enum 삭제, CurrentWave/Wave0SpawnedCount 필드 제거 |
| 2 | `Assets/Scripts/Server/Systems/Wave/WaveManagerSystem.cs` | switch 제거, BlobAsset 기반 전환 로직, InitialSpawnedCount 리셋 |
| 2 | `Assets/Scripts/Server/Systems/Wave/EnemySpawnerSystem.cs` | switch 제거, BlobAsset 기반 스폰 + SelectEnemyPrefab 수정 |
| 2 | `Assets/Scripts/Server/Systems/Initialize/GamePhaseInitSystem.cs` | CurrentWaveIndex=0, InitialSpawnedCount=0 |
| 3 | `Assets/Scripts/Shared/Singletons/GameSettings.cs` | Wave/스폰 필드 14개 제거 |
| 3 | `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | 대응 필드 + Baker 매핑 제거 |
| 3 | `Docs/Architecture/game-rules.md` | Wave System 섹션 업데이트 |
| 3 | `Docs/Systems/Project-SOS 상태 시스템 설계.md` | GamePhaseState/WavePhase 섹션 업데이트 |
| 3 | `Docs/Systems/코드베이스 구조.md` | WavePhase 참조 업데이트 |
| 3 | `CLAUDE.md` | GameSettings 카테고리 업데이트 |

## 검증 방법

1. 전체 프로젝트 컴파일 성공
2. 서버 시작 시 Wave0 Burst 스폰 동작 (Big 30마리)
3. 60초 또는 15킬 후 Wave1 전환 + 주기적 스폰 시작
4. 120초 또는 30킬 후 Wave2 전환 + Flying 등장
5. 인스펙터에서 Wave 항목 추가/삭제/수정 후 동작 확인
6. MaxEnemyCount 제한 동작 확인

## 롤백 전략

- Phase 1 실패: 신규 파일 삭제 + GamePhaseState 원복 (`git checkout`)
- Phase 2 실패: 시스템 3개 원복 (`git checkout` 대상 파일)
- Phase 3 실패: GameSettings/GameSettingsAuthoring 원복 + 문서 원복
- 전체 롤백: `git stash` 또는 `git checkout .` (Phase별 커밋 권장)
