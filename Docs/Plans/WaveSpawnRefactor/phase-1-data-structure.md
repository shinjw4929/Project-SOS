# Phase 1: 데이터 구조 정의 + Authoring/Baker

## 목표

- Wave 설정을 데이터 주도로 관리하기 위한 BlobAsset 기반 구조 정의
- Inspector에서 리스트로 Wave를 편집할 수 있는 Authoring/Baker 구현
- GamePhaseState를 int 인덱스 기반으로 변경

## 선행 조건

- 없음 (첫 Phase)

## 작업 목록

### Task 1: WaveSpawnSettings 정의

**신규 파일**: `Assets/Scripts/Shared/Singletons/WaveSpawnSettings.cs`

- [ ] `SpawnMode` enum 정의 (byte, Shared 어셈블리):
  ```
  Burst = 0,   // 한번에 스폰
  Periodic = 1 // 주기적 스폰
  ```
- [ ] `WaveConfig` struct 정의 (blittable):
  ```
  TriggerTime: float
  TriggerKillCount: int
  SpawnMode: SpawnMode
  SpawnCount: int
  SpawnInterval: float
  SpawnSpacing: float
  SmallRate: float
  BigRate: float
  ```
- [ ] `WaveConfigBlob` struct 정의:
  ```
  Waves: BlobArray<WaveConfig>
  ```
- [ ] `WaveSpawnSettings` IComponentData 정의:
  ```
  Config: BlobAssetReference<WaveConfigBlob>
  MaxEnemyCount: int
  ```

### Task 2: WaveSpawnSettingsAuthoring + Baker 구현

**신규 파일**: `Assets/Scripts/Authoring/Settings/WaveSpawnSettingsAuthoring.cs`

- [ ] `SpawnMode` enum은 Task 1에서 Shared에 정의 완료 — Authoring에서 직접 사용
- [ ] `WaveEntry` Serializable struct 정의:
  ```csharp
  [Serializable]
  public struct WaveEntry
  {
      [Header("Transition")]
      [Tooltip("이 Wave 활성화 경과 시간 (초). Wave0=0")]
      public float triggerTime;
      [Tooltip("이 Wave 활성화 처치 수. Wave0=0")]
      public int triggerKillCount;

      [Header("Spawn")]
      public SpawnMode spawnMode = SpawnMode.Periodic;
      [Tooltip("Burst: 총 수, Periodic: 1회 수")]
      public int spawnCount = 3;
      [Tooltip("Periodic 스폰 주기 (초)")]
      public float spawnInterval = 5f;
      [Tooltip("배치 간격 (m)")]
      public float spawnSpacing = 3f;

      [Header("Enemy Rates")]
      [Range(0f, 1f)] public float smallRate;
      [Range(0f, 1f)] public float bigRate = 1f;
      // Flying = 1 - smallRate - bigRate (Inspector에서 표시용 ReadOnly)
  }
  ```
- [ ] `WaveSpawnSettingsAuthoring` MonoBehaviour:
  ```
  [Header("Global")]
  maxEnemyCount: int = 1200

  [Header("Wave Configs")]
  waves: List<WaveEntry> (기본값 3개 = 현재 Wave0/1/2와 동일)
  ```
- [ ] Baker 구현:
  - `BlobBuilder`로 `WaveConfigBlob` 생성
  - `List<WaveEntry>` → `BlobArray<WaveConfig>` 변환
  - `WaveSpawnSettings` 싱글톤으로 등록
  - 비율 합 검증: SmallRate + BigRate > 1.0이면 **에러 로그 + 1.0으로 클램프** (BigRate를 줄여 합=1.0). Flying 비율 = max(0, 1 - SmallRate - BigRate)이므로 합 < 1이면 Flying 자동 배정됨을 Inspector Tooltip에 명시
  - 빈 리스트 검증 (waves.Count == 0이면 에러 로그, 베이킹 스킵)

### Task 3: GamePhaseState 필드 추가

**수정 파일**: `Assets/Scripts/Shared/Singletons/GamePhaseState.cs`

- [ ] `GamePhaseState`에 새 필드 추가 (기존 필드 유지):
  - `CurrentWaveIndex: int` 추가
  - `InitialSpawnedCount: int` 추가
  - 기존 `CurrentWave`, `Wave0SpawnedCount` 필드는 유지 (Phase 2에서 제거)
  - `WavePhase` enum 유지 (Phase 2에서 삭제)
- [ ] 나머지 필드 유지 (ElapsedTime, TotalKillCount, LastSpawnTime)

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1: WaveSpawnSettings 정의 | 없음 |
| Agent B | Task 2: Authoring + Baker | Task 1 완료 후 |
| Agent C | Task 3: GamePhaseState 수정 | 없음 |

> Task 1과 Task 3은 독립적이므로 병렬 가능. Task 2는 Task 1의 타입에 의존.

## 테스트 요구사항

### EditMode Test

이 Phase에서는 컴파일 확인이 주요 검증. BlobAsset Baker 테스트는 PlayMode 필요.

### PlayMode Test (Phase 2에서 통합 테스트)

이 Phase에서는 생략. Phase 2에서 시스템과 함께 통합 테스트.

## 검증 방법

1. 전체 프로젝트 컴파일 성공 (WavePhase enum과 기존 필드는 유지하므로 컴파일 깨지지 않음)
2. EntitiesSubScene에 WaveSpawnSettingsAuthoring 부착 (GameSettingsAuthoring과 같은 GameObject 또는 별도 GameObject)
3. Inspector에서 Wave 리스트 편집 가능 확인

## 완료 기준

- [ ] SpawnMode enum, WaveConfig, WaveConfigBlob, WaveSpawnSettings 정의 완료
- [ ] WaveSpawnSettingsAuthoring + Baker 구현 완료
- [ ] GamePhaseState에 CurrentWaveIndex, InitialSpawnedCount 필드 추가
- [ ] EntitiesSubScene에 WaveSpawnSettingsAuthoring 부착 (기존 Settings Authoring 패턴 참조)
- [ ] 전체 프로젝트 컴파일 성공
