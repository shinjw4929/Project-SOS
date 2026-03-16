# 로깅 시스템 Tier 1 구현 기록

**날짜**: 2026-03-13
**작업**: 3단계 로깅 시스템 중 Tier 1 (Unity 내부 로컬 파일 로깅) 구현

---

## 변경 파일 목록

### 신규 파일

| 파일 | 설명 |
|------|------|
| `Assets/Scripts/Shared/Logging/LogCategory.cs` | `LogCategory` enum (Combat/Movement/Network/Economy/Wave/System) + `LogWorld` enum (Server/Client) |
| `Assets/Scripts/Shared/Logging/GameLogger.cs` | Burst 호환 로깅 래퍼 유틸리티. `[BurstCompile]` 클래스, 개별 메서드는 `[MethodImpl(AggressiveInlining)]` (BC1064 방지) |
| `Docs/계획/로깅 Tier2 MongoDB.md` | Tier 2 MongoDB 인프라 계획 (스키마, 인덱스, Vector 설정, 쿼리 패턴) |
| `Docs/계획/로깅 Tier3 ClickHouse Grafana.md` | Tier 3 ClickHouse + Grafana 인프라 계획 (테이블, MV, 대시보드, 알림) |
| `Docs/계획/로깅 시스템 3단계 구축 계획.md` | 전체 3단계 로깅 시스템 구축 계획서 |

### 수정 파일

| 파일 | 변경 내용 |
|------|----------|
| `Packages/manifest.json` | `"com.unity.logging": "1.3.10"` 패키지 추가 |
| `Assets/Scripts/Shared/Shared.asmdef` | `"Unity.Logging"` 어셈블리 참조 추가 |
| `Assets/Scripts/GameBootStrap.cs` | LoggerConfig 초기화 (FileSink 10MB x 5롤링 + UnityDebugLog Warning 이상), `using Unity.Logging.Sinks` 추가, `Unity.Logging.Logger` 정규화 |
| `Assets/Scripts/Server/Systems/Wave/WaveManagerSystem.cs` | Wave0→Wave1, Wave1→Wave2 전환 시 Info 로그 (시간/킬수 포함) |
| `Assets/Scripts/Server/Systems/Combat/HeroDeathDetectionSystem.cs` | Hero 사망 Warning (networkId 포함) + GameOver Warning |
| `Assets/Scripts/Server/GoInGameServerSystem.cs` | 클라이언트 접속 Info (networkId 포함), 기존 주석 Debug.Log 대체 |
| `Assets/Scripts/Client/Systems/Initialize/GoInGameClientSystem.cs` | 서버 연결 Info, 기존 주석 Debug.Log 대체 |
| `Assets/Scripts/Server/Systems/Combat/DamageApplySystem.cs` | 프레임당 킬 집계 Info (killCount + totalKillCount) |
| `Assets/Scripts/Server/Systems/Commands/Construction/HandleBuildRequestSystem.cs` | 건설 성공 Info (networkId, grid 좌표), ExecuteBuildRequestJob 내부 |
| `Assets/Scripts/Server/Systems/Commands/Production/HandleProduceUnitRequestSystem.cs` | 생산 시작 Info (unitIdx, networkId) |
| `Assets/Scripts/Server/Systems/Movement/PathfindingSystem.cs` | 경로 실패 Warning (5초 윈도우 집계: failCount/requestCount) |
| `Assets/Scripts/Server/Systems/Wave/EnemySpawnerSystem.cs` | Wave0 초기 스폰 Debug + 주기적 스폰 Debug (spawnCount, wave phase) |
| `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` | 적 stuck 감지 Debug (위치, 목적지, 원인: noPath/partial/collision) |
| `Assets/Scripts/Server/Systems/Commands/Construction/BuildArrivalSystem.cs` | 건설 이동 포기 Debug (유닛 위치, 건설 목표 위치) |
| `Docs/코드베이스 구조.md` | `Shared/Logging/` 폴더 항목 추가 |

---

## 기술적 결정 사항

### 1. com.unity.logging API 주의점

- `WriteTo.File()` 및 `WriteTo.UnityDebugLog()` 확장 메서드는 `Unity.Logging.Sinks` 네임스페이스에 위치
  - `using Unity.Logging;`만으로는 접근 불가, `using Unity.Logging.Sinks;` 필요
- `Logger` 타입이 `Unity.Logging.Logger`와 `UnityEngine.Logger` 사이에 모호
  - `new Unity.Logging.Logger(config)`로 정규화하여 해결

### 2. Burst 호환성 전략

- `GameLogger` 클래스: `[BurstCompile]` (클래스 레벨)
- 개별 메서드: `[MethodImpl(AggressiveInlining)]` (BC1064 방지)
  - `GetPrefix()`: FixedString32Bytes 반환 (struct by value → `[BurstCompile]` 불가)
  - `Debug/Info/Warning/Error()`: FixedString128Bytes를 `in` (readonly ref)으로 수신
- 모든 로그 메시지는 `FixedString128Bytes`로 구축 → `FixedString512Bytes`에 prefix 결합 → `Log.Info()` 호출
- **메시지 조립 헬퍼**: `Field(ref msg, key, value)`, `Pos(ref msg, key, float3)` — 수동 Append 반복 제거

### 3. 로그 성능 원칙

- **매 프레임 대량 발생 이벤트**: 로깅하지 않음 (개별 공격, 이동 틱 등)
- **상태 전환 이벤트**: 로깅 (Wave 전환, Hero 사망, 접속/해제)
- **집계 이벤트**: 프레임당 또는 시간 윈도우 기반 집계 후 로깅 (킬 수, 경로 실패)
- **PathfindingSystem 특별 처리**: 5초 윈도우 집계로 로그 스팸 방지

### 4. 로그 레벨 기준

| 레벨 | 기준 | 이벤트 |
|------|------|--------|
| **Warning** | 이상 징후 | Enemy stuck, Build retry, Build giveup, Path failures (5s) |
| **Info** | 상태 변화 | Wave 전환, 접속, 건설 성공, 생산 시작, Hero died, GameOver |
| **Debug** | 반복 이벤트 | Kills, Spawns |

### 5. 로그 출력 형식

```
WARNING [S:Movement] Enemy stuck, idx=42, pos=(15,23), dest=(30,10), cause=partial
WARNING [S:Economy] Build retry, idx=12, try=1, pos=(8,15), site=(10,18)
WARNING [S:Economy] Build giveup, idx=12, pos=(8,15), site=(10,18)
WARNING [S:Movement] Path failures (5s), failed=12, total=200
INFO    [S:Wave] Wave0 -> Wave1, time=60, kills=15
INFO    [S:Combat] Hero died, networkId=1
INFO    [S:Combat] GameOver - all heroes dead
INFO    [S:Network] Client connected, networkId=1
INFO    [C:Network] Connected to server
INFO    [S:Economy] Build succeeded, networkId=1, gridX=3, gridY=5
INFO    [S:Economy] Production started, unitIdx=2, networkId=1
DEBUG   [S:Combat] Kills, frame=5, total=30
DEBUG   [S:Wave] Wave0 spawned, count=30
DEBUG   [S:Wave] Periodic spawned, count=4, wave=2
```

- 접두사: `[S:카테고리]` (서버) / `[C:카테고리]` (클라이언트)
- `GameLogger.Field(ref msg, key, value)` / `GameLogger.Pos(ref msg, key, float3)` 헬퍼로 구조화

### 5. 파일 경로 및 로테이션

- **경로**: `Application.persistentDataPath/Logs/{yyyy-MM-dd}/game_{HHmmss}.log` (일자별 폴더)
  - Windows: `%APPDATA%/LocalLow/{CompanyName}/Project-SOS/Logs/2026-03-14/game_120000.log`
- **로테이션**: 파일당 10MB, 최대 5개 롤링 (세션당 50MB 상한)
- **일자별 정리**: 게임 시작 시 30일 초과 폴더 자동 삭제 (`CleanOldLogs`)
- **콘솔 출력**: Warning 이상만 Unity Console에 표시

---

## 검증 체크리스트

- [ ] Editor PlayMode 진입 → `persistentDataPath/Logs/`에 로그 파일 생성 확인
- [ ] Wave 전환 로그 출력 확인 (Wave0→Wave1, Wave1→Wave2)
- [ ] 클라이언트 접속 로그 출력 확인 (Server + Client 양측)
- [ ] Hero 사망 시 Warning 로그 확인
- [ ] 건설/생산 시 Economy 로그 확인
- [ ] 경로 실패 시 5초 윈도우 집계 Warning 확인
- [ ] 적 스폰 로그 확인 (Wave0 초기 + 주기적)
- [ ] Burst Inspector에서 GameLogger 호출 포함 시스템 컴파일 확인
- [ ] 1000+ 엔티티 전투 시 프레임 드롭 없음 (프로파일러 비교)

---

## 후속 작업 (Tier 2/3)

Tier 1 검증 완료 후 진행:

1. **Tier 2 (MongoDB)**: Vector Agent 설정 → 로컬 파일 tail → MongoDB 전송
   - 상세: `Docs/계획/로깅 Tier2 MongoDB.md`
2. **Tier 3 (ClickHouse + Grafana)**: Vector fan-out → ClickHouse 동시 전송 → Grafana 대시보드
   - 상세: `Docs/계획/로깅 Tier3 ClickHouse Grafana.md`
