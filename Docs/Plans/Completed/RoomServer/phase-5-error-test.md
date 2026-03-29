# Phase 5: 에러 처리 + 통합 테스트

## 목표

모든 에러 시나리오를 체계적으로 처리하고, 전체 흐름(로비 → 방 → 게임 → 복귀)의 통합 테스트를 수행한다.

## 선행 조건

- Phase 3 + Phase 4 완료

---

## 작업 목록

### Task 1: RoomClient 에러 처리

- [ ] 룸 서버 접속 실패:
  - 3회 재시도 (1초, 2초, 4초 간격, exponential backoff)
  - 3회 실패 시 Error 상태 → UI에 재시도 버튼 표시
- [ ] 대기 중 룸 서버 연결 끊김:
  - 자동 재접속 시도 (3회)
  - 재접속 성공 시 로비 상태로 복귀 (방은 이미 삭제되었을 수 있음)
  - 재접속 실패 시 Error 상태
- [ ] GameStart 후 게임 서버 접속 실패:
  - NetcodeConnectionUtil.Connect() 실패 감지
  - Error 상태 → "게임 서버 접속에 실패했습니다" + 로비 복귀 버튼
- [ ] 비정상 수신 데이터 (잘못된 Envelope):
  - 파싱 실패 시 해당 메시지 스킵 + 로그 경고
  - 연결은 유지

### Task 2: 토큰 만료 처리

- [ ] 토큰 TTL = 60초 (룸 서버 설정)
- [ ] GameStart 수신 후 60초 이내에 게임 서버 접속 + GoInGameRequestRpc 전송 필요
- [ ] 만약 접속 지연으로 60초 초과 시:
  - TokenValidationSystem에서 검증 실패 → Disconnect
  - 클라이언트: Disconnect 감지 → "접속 시간 초과" 에러 → 로비 복귀

### Task 3: 게임 종료 후 재플레이

- [ ] 게임 종료 시 (GameOverRpc 수신 또는 Disconnect):
  1. Netcode 연결 끊기
  2. RoomAuthState 싱글톤 초기화 (이전 토큰 제거)
  3. RoomClient.ReturnToLobby() 호출
  4. RoomClient가 룸 서버에 재접속
  5. 상태 → Lobby, 방 목록 화면으로 복귀

### Task 4: 서버 측 에러 처리 보강

- [ ] TokenValidationSystem: RoomTokenValidator 연결 실패 시
  - 로그 에러 + 해당 RPC의 클라이언트 Disconnect
  - 재연결 시도 (다음 OnUpdate에서)
- [ ] SlotNotifySystem: SlotNotifyClient 연결 실패 시
  - 로그 경고 후 무시 (룸 서버 측 TTL 90초로 자동 복구)
  - 재연결 시도

### Task 5: 문서 업데이트

- [ ] `Docs/Systems/코드베이스 구조.md` — 신규 폴더(Client/Network, Server/Network, Shared/Network) 및 파일 추가
- [ ] `Docs/Systems/시스템 그룹 및 의존성.md` — TokenValidationSystem, SlotNotifySystem 추가 (SimulationSystemGroup, ServerSimulation)
- [ ] `Docs/Architecture.md` — 접속 흐름(AutoConnect → Room Server 경유), 토큰 검증 패턴, 슬롯 관리 패턴 추가

### Task 6: 통합 테스트 시나리오

| # | 시나리오 | 예상 결과 |
|---|---------|----------|
| 1 | 정상 흐름: 로비 → 방 생성 → 다른 클라이언트 참가 → 전원 준비 → 시작 → 게임 진입 | Hero 생성, 게임 플레이 가능 |
| 2 | 토큰 없이 :7979 직접 접속 | 즉시 Disconnect |
| 3 | 만석 방 참가 시도 | RejectResponse(ROOM_FULL), UI 에러 표시 |
| 4 | 미준비 상태에서 시작 시도 | RejectResponse(NOT_ALL_READY) |
| 5 | 룸 서버 중단 후 재시작 | 클라이언트 자동 재접속 → 로비 |
| 6 | 게임 중 클라이언트 연결 끊김 | SlotReleased 전송, 룸 서버 슬롯 해제 |
| 7 | 게임 종료 → 재플레이 | 로비 복귀, 새 방 생성 가능 |
| 8 | 호스트 이탈 (대기실) | 방 삭제, 나머지 로비 복귀 |
| 9 | GameStart 후 60초 초과 | 토큰 만료 → Disconnect → 로비 복귀 |
| 10 | 게임 서버 비정상 종료 | 룸 서버 TTL 90초 만료 → 세션 자동 정리 |

- [ ] 시나리오 1~4: 수동 테스트 + 로그 검증
- [ ] 시나리오 5~10: 수동 테스트 (프로세스 강제 종료 등)

---

## 에러 처리 요약표

| 시나리오 | 처리 주체 | 처리 방법 |
|---------|----------|----------|
| 룸 서버 접속 실패 | RoomClient | 3회 재시도 → Error 상태 |
| 대기 중 연결 끊김 | RoomClient | 자동 재접속 → 로비 복귀 |
| GameStart 후 게임 서버 접속 실패 | RoomClient | Error 상태 → 로비 복귀 |
| 토큰 만료 (60초) | TokenValidationSystem | 검증 실패 → Disconnect |
| 게임 중 연결 끊김 | Netcode 기본 + SlotNotifySystem | Disconnect + SlotReleased |
| 룸 없이 직접 접속 | TokenValidationSystem | 빈 토큰 → Disconnect |
| 호스트 이탈 | 룸 서버 (C++) | 방 삭제 → ROOM_CLOSED |
| 게임 서버 비정상 종료 | 룸 서버 (C++) | TTL 90초 만료 → 자동 정리 |
| 게임 종료 후 재플레이 | RoomClient | ReturnToLobby() → 재접속 |

---

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 + 2 (클라이언트 에러 처리) | Phase 2, 3 |
| Agent B | Task 3 + 4 (재플레이 + 서버 측) | Phase 2, 3 |
| Agent C | Task 5 (문서 업데이트) | Phase 1~4 코드 확정 후 |
| Main | Task 6 (통합 테스트) | Task 1~5 완료 후 |

---

## 테스트 요구사항

### PlayMode Test

- 룸 서버 미실행 상태에서 앱 시작 → 3회 재시도 → Error 상태
- 게임 종료 → ReturnToLobby() → 로비 화면 복귀
- 토큰 만료 시뮬레이션 (60초 대기 후 접속) → Disconnect

---

## 검증 방법

1. 통합 테스트 시나리오 #1~#10 전체 수행 및 통과
2. 에러 발생 시 사용자에게 명확한 메시지 표시
3. 에러 복구 후 정상 흐름 재진입 가능
4. 로그에 에러 처리 과정이 기록됨

## 완료 기준

- [x] 모든 에러 시나리오에 대한 처리 코드 구현
- [ ] 통합 테스트 시나리오 #1~#10 통과 (룸 서버 실행 환경에서 수동 검증 필요)
- [x] 에러 복구 후 정상 흐름 재진입 확인 (코드 구현 완료)
- [x] 게임 종료 → 재플레이 흐름 정상 동작 (OnGameOver + ReturnToLobby)
- [x] 전체 프로젝트 컴파일 성공
