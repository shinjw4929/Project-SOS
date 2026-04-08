# Phase 3: GameSettings 정리 + 문서 업데이트

## 목표

- GameSettings/GameSettingsAuthoring에서 Wave/스폰 관련 필드 14개를 제거하여 관심사 분리 완성
- 관련 문서를 현재 구조에 맞게 업데이트

## 선행 조건

- Phase 2 완료 (시스템이 더 이상 GameSettings의 Wave/스폰 필드를 참조하지 않음)

## 작업 목록

### Task 1: GameSettings 필드 제거

**수정 파일**: `Assets/Scripts/Shared/Singletons/GameSettings.cs`

- [ ] 다음 필드 삭제 (14개):
  ```
  // === Wave 설정 === 섹션 전체
  Wave0InitialSpawnCount
  Wave1TriggerTime
  Wave1TriggerKillCount
  Wave2TriggerTime
  Wave2TriggerKillCount
  Wave1SpawnInterval
  Wave1SpawnCount
  Wave2SpawnInterval
  Wave2SpawnCount
  MaxEnemyCount

  // === 스폰 === 섹션 전체
  EnemyBigSpawnRate
  EnemySmallOnlyRate
  Wave0SpawnSpacing
  PeriodicSpawnSpacing
  ```
- [ ] 주석 섹션 정리 (남은 카테고리만 유지)

### Task 2: GameSettingsAuthoring 필드 제거

**수정 파일**: `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs`

- [ ] 다음 Inspector 필드 삭제:
  ```
  // [Header("Wave0 Settings")] 섹션
  wave0InitialSpawnCount

  // [Header("Wave Transition Conditions")] 섹션
  wave1TriggerTime, wave1TriggerKillCount, wave2TriggerTime, wave2TriggerKillCount

  // [Header("Enemy Limit")] 섹션
  maxEnemyCount

  // [Header("Spawn Balance")] 섹션
  enemyBigSpawnRate, enemySmallOnlyRate, wave0SpawnSpacing, periodicSpawnSpacing

  // [Header("Wave1+ Spawn Settings")] 섹션
  wave1SpawnInterval, wave1SpawnCount, wave2SpawnInterval, wave2SpawnCount
  ```
- [ ] Baker의 `AddComponent` 블록에서 대응 매핑 제거:
  ```
  Wave0InitialSpawnCount = authoring.wave0InitialSpawnCount,
  Wave1TriggerTime = authoring.wave1TriggerTime,
  ... (14행)
  ```
- [ ] 삭제 전 Grep으로 다른 시스템이 이 필드를 참조하지 않는지 최종 확인

### Task 3: 문서 업데이트

- [ ] `Docs/Architecture/game-rules.md`:
  - Wave System 테이블을 "인스펙터에서 설정" 방식으로 변경
  - GameSettings 카테고리에서 Wave/스폰 항목 제거, WaveSpawnSettings 추가
- [ ] `Docs/Systems/Project-SOS 상태 시스템 설계.md`:
  - WavePhase enum → CurrentWaveIndex int 반영
  - GamePhaseState 필드 목록 업데이트
- [ ] `Docs/Systems/시스템 그룹 및 의존성.md`:
  - WaveManagerSystem, EnemySpawnerSystem의 RequireForUpdate 변경 반영
  - WaveSpawnSettings 싱글톤 추가
- [ ] `Docs/Systems/코드베이스 구조.md`:
  - WavePhase 참조 업데이트, WaveSpawnSettings 싱글톤 추가
- [ ] `CLAUDE.md`:
  - GameSettings 카테고리에서 Wave/스폰 항목 제거, WaveSpawnSettings 추가
- [ ] `Docs/Documentation-Checklist.md`:
  - GameSettings 카테고리에서 Wave/스폰 항목 제거, WaveSpawnSettings 반영

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 + Task 2: GameSettings/Authoring 정리 | 없음 |
| Agent B | Task 3: 문서 업데이트 | 없음 |

> 코드 변경과 문서 변경은 독립적이므로 병렬 가능.

## 테스트 요구사항

### EditMode Test

- 이 Phase는 필드 삭제와 문서 업데이트만 포함하므로 별도 테스트 불필요

### PlayMode Test

- Phase 2 테스트 재실행으로 회귀 검증

## 검증 방법

1. 전체 프로젝트 컴파일 성공
2. GameSettings에서 Wave/스폰 관련 필드 Grep 0건
3. `GameSettingsAuthoring` Inspector에서 Wave 관련 섹션 없음
4. `WaveSpawnSettingsAuthoring` Inspector에서 모든 Wave/스폰 설정 확인 가능
5. 문서가 현재 코드와 일치

## 완료 기준

- [ ] GameSettings에서 Wave/스폰 필드 14개 제거
- [ ] GameSettingsAuthoring에서 대응 필드 + Baker 매핑 제거
- [ ] game-rules.md 업데이트
- [ ] 상태 시스템 설계.md 업데이트
- [ ] 시스템 그룹 및 의존성.md 업데이트
- [ ] 코드베이스 구조.md 업데이트
- [ ] CLAUDE.md GameSettings 카테고리 업데이트
- [ ] 전체 컴파일 성공
