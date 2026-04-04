# Chat Client 연동 오케스트레이션 플랜

## 문제 정의
- Chat Server(C++ / Boost.Asio / TCP + Protobuf)가 완성되어 있으나, Unity 클라이언트 측 연동 코드가 전혀 없음
- 현재 인게임 채팅, 로비 채팅, 귓속말 기능이 불가능한 상태
- Room Server 연동(RoomClient.cs)은 이미 완료되어 패턴이 확립됨 — 동일 패턴을 재사용하여 일관성 확보

## AS-IS (현재 상태)

### 서버 측 (완료)
- Chat Server: TCP :8082 (클라이언트), :8083 (내부 Room Server 연동)
- 프로토콜: `proto/chat.proto` (ChatEnvelope + oneof payload)
- 채널: LOBBY(전체), TEAM(팀), ALL(세션 내), WHISPER(1:1, 후순위 구현)
- 채널 참가: 서버가 인증 시 자동 처리 (JoinChannel/LeaveChannel 클라이언트 사용 불필요)
- 인증: ChatAuth(player_id, session_id, team_id, player_name) → ChatAuthResult (로비/세션 모드)
- 히스토리: Redis LIST, 세션 입장 시 최근 20개 전송
- Rate Limit: 10msg/5sec per player_id
- 메시지 제한: 200 bytes UTF-8
- 세션 생명주기: Room Server → SessionCreated/SessionEnded 내부 메시지

### 클라이언트 측 (미구현)
- `Assets/Scripts/Client/Network/` 에 ChatClient 없음
- Chat UI 없음
- 기존 입력 시스템에 채팅 포커스 가드 없음

### 참조 가능한 기존 패턴 (RoomClient.cs)
| 항목 | 구현 상태 | 파일 |
|---|---|---|
| TCP 연결 관리 | 완료 | `RoomClient.cs` |
| Protobuf 프레이밍 (4B LE) | 완료 (Room 전용 — `Sos.Room.Envelope` 하드코딩) | `ProtobufFraming.cs` |
| 지수 백오프 재연결 | 완료 | `RoomClient.cs` |
| DontDestroyOnLoad 싱글톤 | 완료 | `RoomClient.cs` |
| 하트비트 코루틴 | 완료 | `RoomClient.cs` |
| ECS 연동 (RoomAuthState) | 완료 | `RoomAuthState.cs` |

> **주의**: `ProtobufFraming.cs`는 `Sos.Room.Envelope`에 하드코딩되어 있어 `ChatEnvelope`에 직접 사용 불가. ChatClient용 `ChatProtobufFraming.cs`를 동일 패턴으로 별도 작성하거나, 기존 ProtobufFraming을 제네릭으로 리팩토링해야 함.

## TO-BE (목표 상태)

### 네트워크 계층
- `ChatClient.cs`: RoomClient와 동일한 TCP/Protobuf/재연결 패턴으로 Chat Server 연결
- `Chat.cs`: protoc 코드젠 출력 (ChatEnvelope, ChatAuth, ChatSend, ChatReceive 등)
- 인증 흐름: 로비 접속 시 자동 ChatAuth(session_id 빈 값) → 게임 서버 접속 후 NetworkId 확정 시 session_id + team_id 포함 재인증
- 하트비트: 10초 간격 ChatHeartbeat

### UI 계층
- `ChatUIController.cs`: 입력 필드 + 메시지 표시 (50개 링버퍼) + 채널 탭
- Enter 키 토글: 채팅 입력 활성화/비활성화
- `static bool IsChatFocused`: 다른 입력 시스템에서 참조
- TMP Rich Text 비활성화 (XSS 방지)

### 입력 시스템 통합
- 5개 파일에 `ChatUIController.IsChatFocused` 가드 삽입
- 마우스 입력은 제한하지 않음 (엣지 패닝, 줌, 선택 유지)

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|---|---|---|
| 채팅 프로토콜 | 서버만 구현 | 서버 + 클라이언트 양방향 |
| 로비 채팅 | 불가 | LOBBY 채널 실시간 채팅 |
| 인게임 채팅 | 불가 | TEAM + ALL 채널 + WHISPER |
| 채팅 UI | 없음 | 입력 필드 + 메시지 표시 + 채널 탭 |
| 입력 충돌 | 해당 없음 | IsChatFocused 가드로 키보드 분리 |
| 채팅 히스토리 | 서버 Redis 저장만 | 세션 입장 시 클라이언트에 20개 전송 + 표시 |
| 연결 안정성 | 해당 없음 | 지수 백오프 재연결 + 하트비트 |

## Phase 체크리스트

### Phase 1: Protobuf 코드젠 + ChatClient 네트워크 계층
- [ ] chat.proto → Chat.cs 코드젠
- [ ] ChatProtobufFraming.cs 작성 (ChatEnvelope용 4B LE 프레이밍)
- [ ] ChatClient.cs TCP 연결/해제/재연결
- [ ] ChatAuth 인증 (로비/세션 모드, team_id 포함)
- [ ] 메시지 송수신 (ChatSend/ChatReceive)
- [ ] 하트비트 코루틴
- [ ] ChatError 핸들링
- [ ] 게임 서버 접속 연동 (세션 재인증, sessionId + teamId=NetworkId.Value)
→ 상세: [phase-1-네트워크계층.md](./phase-1-네트워크계층.md)

### Phase 2: ChatUIController + 입력 시스템 통합
- [ ] ChatUIController MonoBehaviour (입력 필드, 메시지 표시, 채널 탭 — TEAM 포함)
- [ ] 50개 메시지 링버퍼
- [ ] Enter 키 토글 + IsChatFocused static 프로퍼티
- [ ] TMP Rich Text 비활성화
- [ ] 기존 입력 시스템 5개 파일 IsChatFocused 가드 삽입
- [ ] ChatClient ↔ ChatUIController 이벤트 바인딩
→ 상세: [phase-2-UI및입력통합.md](./phase-2-UI및입력통합.md)

### Phase 3: 통합 테스트 + 엣지 케이스 처리
- [ ] 로비 → 인게임 → 로비 전환 시나리오 검증
- [ ] 재연결 시 채널 상태 복구 확인
- [ ] Rate Limit 초과 시 UI 피드백
- [ ] 메시지 길이 초과 클라이언트 사전 검증
- [ ] 500+ 유닛 전투 중 채팅 성능 프로파일링
→ 상세: [phase-3-통합테스트.md](./phase-3-통합테스트.md)

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |
| 2 | Phase 1 (ChatClient 이벤트 인터페이스) | X |
| 3 | Phase 1 + 2 완료 | X |

## 변경 파일 요약

| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Assets/Scripts/Shared/Network/Generated/Chat.cs` | 신규 — protoc 코드젠 |
| 1 | `Assets/Scripts/Shared/Network/ChatProtobufFraming.cs` | 신규 — ChatEnvelope용 4B LE 프레이밍 유틸 |
| 1 | `Assets/Scripts/Client/Network/ChatClient.cs` | 신규 — TCP 연결, 인증, 메시지, 하트비트 |
| 1 | `Assets/Scripts/Client/Network/RoomClient.cs` | 수정 — 세션 재인증 트리거 (방법에 따라 수정 범위 변동, GameOver 연동은 ChatClient가 GameOverEvents.OnGameOver 직접 구독) |
| 2 | `Assets/Scripts/Client/Controller/UI/ChatUIController.cs` | 신규 — 채팅 UI 전체 |
| 2 | `Assets/Scripts/Client/Systems/Commands/UnitControl/UnitCommandInputSystem.cs` | 수정 — IsChatFocused 가드 |
| 2 | `Assets/Scripts/Client/Systems/Commands/StructureAction/StructureCommandInputSystem.cs` | 수정 — IsChatFocused 가드 |
| 2 | `Assets/Scripts/Client/Systems/Commands/Construction/ConstructionMenuInputSystem.cs` | 수정 — IsChatFocused 가드 |
| 2 | `Assets/Scripts/Client/Controller/Camera/CameraSystem.cs` | 수정 — IsChatFocused 가드 (T키만) |
| 2 | `Assets/Scripts/Client/Systems/Commands/Selection/EntitySelectionSystem.cs` | 수정 — IsChatFocused 가드 (ESC) |

## 검증 방법

1. **코드젠 검증**: Chat.cs 컴파일 성공 + ChatEnvelope 직렬화/역직렬화 라운드트립
2. **연결 검증**: ChatClient → Chat Server TCP 연결 + ChatAuth 성공 응답
3. **메시지 검증**: 로비 메시지 송수신, 세션 TEAM/ALL 채널 메시지
4. **히스토리 검증**: 세션 입장 시 최근 20개 메시지 수신 + UI 표시
5. **입력 격리**: 채팅 포커스 중 A/Q/W/E/R/T/ESC 키 무반응 확인
6. **재연결 검증**: 서버 강제 종료 → 지수 백오프 재연결 → 재인증 자동 수행
7. **성능 검증**: 500+ 유닛 전투 중 채팅 메시지 송수신 시 프레임 드롭 없음

## 롤백 전략

- **Phase 1 롤백**: Chat.cs, ChatProtobufFraming.cs, ChatClient.cs 삭제 + RoomClient.cs 수정 부분 revert. 기존 기능에 영향 없음.
- **Phase 2 롤백**: ChatUIController.cs 삭제 + 입력 시스템 5개 파일의 IsChatFocused 가드 제거. Phase 1은 독립적으로 유지 가능.
- **Phase 3 롤백**: 테스트/검증 단계이므로 코드 변경 최소. 발견된 버그 수정만 개별 revert.
