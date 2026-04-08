# Wave 스폰 시스템 리팩토링 요약

## 문제 정의

현재 Wave 시스템이 WavePhase enum 3개와 switch문에 하드코딩되어 있어, 10개 이상 Wave 확장 시 매번 4파일 이상 코드 수정 필요. Wave/스폰 관련 설정 14개가 GameSettings에 산재하여 밸런싱 조절이 어렵고, 적 비율 네이밍이 실제 동작과 불일치.

## Phase 구성 요약

| Phase | 내용 | 핵심 변경 |
|-------|------|----------|
| 1 | 데이터 구조 + Authoring/Baker | BlobAsset 기반 WaveConfig 정의, WaveSpawnSettingsAuthoring 신규, GamePhaseState에 새 필드 추가 |
| 2 | 시스템 리팩토링 + WavePhase 제거 | WavePhase enum 삭제, switch→인덱스 루프, SelectEnemyPrefab 비율 명시화 |
| 3 | GameSettings 정리 + 문서 | Wave/스폰 필드 14개 제거, 관련 문서 4건 업데이트 |

## 예상 영향 범위

- **코드 변경**: GamePhaseState, WaveSpawnSettings(신규), WaveSpawnSettingsAuthoring(신규), WaveManagerSystem, EnemySpawnerSystem, GamePhaseInitSystem, GameSettings, GameSettingsAuthoring (8파일)
- **영향 없음**: DamageApplySystem (TotalKillCount 필드명 유지), EnemyPrefabCatalog (변경 없음)
- **문서 변경**: game-rules.md, 상태 시스템 설계.md, 시스템 그룹 및 의존성.md, 코드베이스 구조.md (4건)
- **네트워크 영향**: 없음 (GamePhaseState는 서버 전용 싱글톤, Ghost 미동기화)

## 자동 리뷰 통과 여부

1회차에 승인. 4건 수정사항 직접 반영 완료:
1. Phase 1에서 WavePhase enum 유지 (컴파일 보장) → Phase 2에서 삭제
2. SpawnMode enum을 Shared 어셈블리에 정의 (매직넘버 제거)
3. Wave 전환 시 InitialSpawnedCount 리셋 로직 추가
4. 코드베이스 구조.md를 Phase 3 문서 목록에 추가
