# Phase 1: Protobuf 코드젠 + ChatClient 네트워크 계층

## 목표
- chat.proto에서 C# 코드 생성
- RoomClient.cs와 동일한 패턴으로 ChatClient.cs 구현
- 게임 서버 접속 후 NetworkId 확정 시 ChatClient 세션 재인증 트리거

## 선행 조건
- Chat Server 구동 가능 (docker-compose up chat-server)
- `Docs/Plans/Completed/ChatServer/chat.proto` 확정 (변경 없음)
- ProtobufFraming.cs 참조 가능 (Room 전용 — ChatEnvelope용 별도 프레이밍 필요)

## 작업 목록

### Task 1: Protobuf C# 코드젠

- [ ] `Docs/Plans/Completed/ChatServer/chat.proto`에서 protoc 실행하여 `Chat.cs` 생성
- [ ] 출력 경로: `Assets/Scripts/Shared/Network/Generated/Chat.cs` (기존 Room.cs 패턴 준수. chat.proto 주석의 Client 경로는 무시)
- [ ] 네임스페이스: `Sos.Chat` (proto package `sos.chat` → C# 변환)
- [ ] protoc 버전/경로: 기존 Room.cs 코드젠에 사용한 protoc 환경과 동일하게 설정
- [ ] 컴파일 확인: Unity Editor에서 에러 없이 빌드
- [ ] ChatEnvelope 직렬화/역직렬화 라운드트립 검증

### Task 1.5: ChatProtobufFraming.cs 작성

- [ ] `Assets/Scripts/Shared/Network/ChatProtobufFraming.cs` 신규 작성
- [ ] ProtobufFraming.cs와 동일한 4B LE 프레이밍 패턴, `ChatEnvelope` 대상
- [ ] `Frame(ChatEnvelope)` + `TryDeframe(buffer, ref offset, available, out ChatEnvelope)` 메서드

### Task 2: ChatClient.cs 기본 구조

- [ ] MonoBehaviour + DontDestroyOnLoad 싱글톤 패턴 (RoomClient 동일)
- [ ] chatServerHost / chatServerPort 설정: ChatClient 자체에 `[SerializeField] string chatServerHost = "127.0.0.1"` + `[SerializeField] ushort chatServerPort = 8082` 기본값 설정. Phase 2에서 ChatUIController 완성 시 외부 주입으로 전환. RoomUIController에 Chat 관련 필드를 추가하지 않음 (SRP 유지)
- [ ] 상태 머신 정의:

```
enum ChatState { Disconnected, Connecting, Lobby, InSession }
```

- [ ] 설정값 상수 정의:

| 상수 | 값 | 용도 |
|---|---|---|
| MaxRetryAttempts | 3 | 최대 재연결 시도 |
| InitialRetryDelay | 1.0f | 첫 재시도 대기 (초) |
| HeartbeatInterval | 10.0f | 하트비트 전송 간격 (초) |
| MaxMessageSize | 1MB | 최대 메시지 크기 |
| InitialBufferSize | 8192 | 수신 버퍼 초기 크기 |

- [ ] 이벤트 인터페이스 정의:

```csharp
public event Action<ChatAuthResult> OnAuthResult;
public event Action<ChatReceive> OnMessageReceived;
public event Action<SystemMessage> OnSystemMessage;
public event Action<ChatError> OnChatError;
public event Action OnConnected;
public event Action OnDisconnected;
```

### Task 3: TCP 연결 관리

- [ ] `ConnectToChatServer(string host, int port)` — async 연결 + 지수 백오프
- [ ] `Disconnect()` — 정리(cancel token, stream close, state reset)
- [ ] `ReceiveLoopAsync()` — RoomClient 동일 패턴 (버퍼 관리, TryDeframe, Dispatch)
- [ ] `HandleDisconnection()` — 상태에 따라 자동 재연결 or 이벤트 발행
- [ ] CancellationTokenSource 관리 (연결/해제 시 정리)

### Task 4: 인증 흐름

- [ ] `SendAuth(string playerId, string playerName, string sessionId = "", uint teamId = 0)` — ChatAuth 전송
- [ ] 로비 인증 시 playerId/playerName을 ChatClient 내부 필드에 저장 (세션 재인증 시 재사용)
- [ ] 로비 모드: ConnectToChatServer 성공 직후 sessionId 빈 값, teamId=0으로 자동 호출
- [ ] 세션 모드: 게임 서버 접속 후 NetworkId 확정 시점에 sessionId + teamId(=NetworkId.Value) 포함하여 재인증
  - teamId는 GameStart 메시지에 없음. GoInGameServerSystem이 `NetworkId.Value`를 Team.teamId로 할당
  - 재인증 트리거: RoomClient.OnGameStartReceived가 아닌, 게임 서버 접속 + NetworkId 확정 후
- [ ] ChatAuthResult 수신 → 성공 시 State 전환, 실패 시 OnAuthResult 이벤트로 사유 전달

### Task 5: 메시지 송수신

- [ ] `SendMessage(ChatChannel channel, string content)` — ChatSend 전송 (WHISPER는 후순위)
- [ ] 클라이언트 사전 검증: content 비어있음 / 200 bytes 초과 차단
- [ ] ChatReceive 수신 → OnMessageReceived 이벤트 발행
- [ ] SystemMessage 수신 → OnSystemMessage 이벤트 발행
- [ ] ChatError 수신 → OnChatError 이벤트 발행

### Task 6: 하트비트

- [ ] `StartHeartbeat()` — 코루틴, 10초 간격 ChatHeartbeat 전송
- [ ] State가 Lobby 또는 InSession일 때만 동작
- [ ] Disconnect 시 코루틴 정지

### Task 7: RoomClient 연동 + 세션 재인증 트리거

- [ ] playerId/playerName 공유: 로비 인증 시 ChatClient 내부에 저장. 원본은 RoomUIController(currentUserId, userNameInput)에 위치하므로, 최초 ChatClient 연결 시 외부에서 주입
- [ ] 세션 재인증 트리거: GameStart 직후가 아닌, 게임 서버 접속 후 NetworkId 확정 시점
  - 방법 A: RoomClient.OnGameStartReceived에서 sessionId만 ChatClient에 캐시 → 게임 서버 접속 후 별도 이벤트/폴링으로 NetworkId 획득 → 재인증
  - 방법 B: GoInGameClientSystem 이후 실행되는 시스템에서 MonoBehaviour 브릿지로 ChatClient에 통보
  - 구현 시 가장 간결한 방법 선택
- [ ] GameOver 시 로비 복귀: `GameOverEvents.OnGameOver` 정적 이벤트 구독 → ChatClient 로비 모드 복귀 (sessionId 빈 값, teamId=0으로 재인증). RoomClient에 의존하지 않고 ChatClient가 직접 이벤트 구독 (GameOverPanelController와 동일 패턴)

### Task 8: 메시지 디스패치

- [ ] `DispatchEnvelope(ChatEnvelope envelope)` — PayloadCase switch:

| PayloadCase | 핸들러 | 동작 |
|---|---|---|
| AuthResult | HandleAuthResult | State 전환 + 이벤트 |
| Receive | HandleReceive | OnMessageReceived |
| System | HandleSystem | OnSystemMessage |
| Error | HandleError | OnChatError |
| Heartbeat | (무시) | keep-alive 확인 |

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Agent A | Task 1 + 1.5: protoc 코드젠 + ChatProtobufFraming | 없음 |
| Agent B | Task 2~6, 8: ChatClient.cs 본체 | Task 1 + 1.5 완료 후 (Chat.cs + ChatProtobufFraming 필요) |
| Agent C | Task 7: 세션 재인증 트리거 + GameOverEvents 연동 | Task 2 완료 후 (ChatClient 이벤트 인터페이스 필요) |

## 테스트 요구사항

### EditMode Test
- ChatEnvelope 직렬화/역직렬화 라운드트립
- 메시지 길이 검증 로직 (200 bytes 경계값, 빈 문자열, 공백만)
- 상태 전환 유효성 (Disconnected → Connecting → Lobby → InSession)

### 수동 검증
- Chat Server 기동 후 ChatClient 연결 → ChatAuth 성공 로그 확인
- 로비 메시지 송수신 확인 (두 클라이언트)
- GameStart 시 세션 재인증 → ALL 채널 메시지 확인

## 검증 방법
- Chat.cs 컴파일 성공
- ChatClient → Chat Server TCP 연결 + ChatAuth 성공 응답 수신
- 로비 메시지 송수신 왕복 확인
- 하트비트 로그 10초 간격 출력 확인
- 서버 강제 종료 시 재연결 시도 로그 확인

## 완료 기준
- [ ] Chat.cs 코드젠 완료 + 컴파일 성공
- [ ] ChatProtobufFraming.cs 작성 완료
- [ ] ChatClient.cs 전체 메서드 구현
- [ ] 세션 재인증 트리거 코드 추가 + GameOverEvents.OnGameOver 구독 연동
- [ ] Chat Server 연결 + 인증 + 메시지 송수신 동작 확인
- [ ] EditMode Test 통과
