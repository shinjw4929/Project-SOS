# Phase 2: 룸 클라이언트 구현

## 목표

룸 서버(:8080)와 TCP 통신하는 `RoomClient` MonoBehaviour를 구현한다. 방 목록 조회, 방 생성/참가/퇴장, 준비 토글, 게임 시작 수신까지 처리한다.

## 선행 조건

- Phase 1 완료 (Protobuf 코드젠 + ProtobufFraming + NetcodeConnectionUtil)

---

## 작업 목록

### Task 1: RoomClient.cs 핵심 구현

**파일 (신규)**: `Assets/Scripts/Client/Network/RoomClient.cs`

- [ ] MonoBehaviour + DontDestroyOnLoad 패턴
- [ ] TCP 비동기 연결 (async/await + System.Net.Sockets.TcpClient)
- [ ] 수신 루프: NetworkStream.ReadAsync → ProtobufFraming.TryDeframe → Envelope.PayloadCase 분기
- [ ] 송신: Envelope 생성 → ProtobufFraming.Frame → NetworkStream.WriteAsync
- [ ] 상태 머신 구현:
  ```
  Disconnected → Connecting → Lobby → InRoom → Matched → InGame
  ```
- [ ] Heartbeat 코루틴 (10초 간격 RoomHeartbeat 전송)
- [ ] CancellationTokenSource로 수신 루프 정리
- [ ] 재접속 흐름: ReturnToLobby() 메서드

#### 수신 메시지 처리

| PayloadCase | 동작 |
|-------------|------|
| CreateRoomResponse | 성공: 상태 → InRoom, 이벤트 발행 / 실패: 에러 이벤트 |
| JoinRoomResponse | 성공: 상태 → InRoom / 실패: 에러 이벤트 |
| RoomListResponse | 방 목록 이벤트 발행 (UI 갱신용) |
| RoomUpdate | 대기실 상태 갱신 이벤트 발행 |
| GameStart | auth_token + session_id 저장 → 상태 → Matched → Netcode 연결 트리거 |
| RejectResponse | 거부 이유별 에러 이벤트 발행 |

#### 송신 메서드

```csharp
public void SendCreateRoom(string userId, string userName, string roomName, uint maxPlayers)
public void SendJoinRoom(string userId, string userName, string roomId)
public void SendLeaveRoom()
public void SendToggleReady()
public void SendStartGame()  // 호스트 전용
public void SendRoomListRequest(uint page = 0, uint pageSize = 20)
```

#### 이벤트 시스템

```csharp
public event Action<RoomInfo> OnRoomCreated;
public event Action<RoomInfo> OnRoomJoined;
public event Action<RoomListResponse> OnRoomListReceived;
public event Action<RoomInfo> OnRoomUpdated;
public event Action<GameStart> OnGameStartReceived;
public event Action<RejectResponse> OnRejected;
public event Action<string> OnError;
public event Action<RoomClientState> OnStateChanged;
```

### Task 2: RoomAuthState 싱글톤

**파일 (신규)**: `Assets/Scripts/Client/Component/Singleton/RoomAuthState.cs`

- [ ] IComponentData 구현
- [ ] 필드:
  ```csharp
  public FixedString64Bytes AuthToken;
  public FixedString64Bytes SessionId;
  // HasToken 불필요 — AuthToken.Length > 0 으로 판별 (bool 필드 대신 값 검사)
  ```
- [ ] RoomClient의 GameStart 수신 시 → RoomAuthState에 토큰 기록
- [ ] ClientBootstrapSystem에서 싱글톤 초기화 (빈 토큰, default FixedString64Bytes)

### Task 3: RoomClient → ECS 연동 (브리징 메커니즘)

- [ ] MonoBehaviour → ECS 싱글톤 브리징:
  ```csharp
  // RoomClient에서 ECS 싱글톤에 토큰 기록
  var world = World.DefaultGameObjectInjectionWorld;
  if (world != null && world.IsCreated)
  {
      var em = world.EntityManager;
      // RoomAuthState 싱글톤 엔티티 찾기 + SetComponentData
  }
  ```
- [ ] GameStart 수신 시:
  1. RoomAuthState 싱글톤에 AuthToken + SessionId 기록 (위 브리징 패턴)
  2. NetcodeConnectionUtil.Connect() 호출 (game_server_host, game_server_port)
  3. 상태 → Matched
- [ ] Netcode 연결 성공 감지 시:
  1. 상태 → InGame
  2. 룸 서버 TCP 연결 종료

### Task 3.5: ClientBootstrapSystem에 RoomAuthState 초기화 추가

**파일**: `Assets/Scripts/Client/Systems/Initialize/ClientBootstrapSystem.cs`

- [ ] 기존 싱글톤 초기화 패턴을 따라 RoomAuthState 초기화 추가:
  ```csharp
  if (!SystemAPI.HasSingleton<RoomAuthState>())
  {
      var entity = entityManager.CreateEntity(typeof(RoomAuthState));
      // default FixedString64Bytes는 Length==0이므로 별도 초기화 불필요
  }
  ```

### Task 4: 생명주기 관리

- [ ] 생성 시점: 앱 시작 씬에서 자동 생성 (씬 배치 또는 RuntimeInitializeOnLoadMethod)
- [ ] DontDestroyOnLoad 적용
- [ ] OnDestroy: CancellationToken 취소, TcpClient.Close(), Heartbeat 중지
- [ ] Application.quitting 이벤트 처리

---

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (RoomClient 핵심) | Phase 1 |
| Agent B | Task 2 (RoomAuthState) | 없음 |
| Main | Task 3 + 4 (연동 + 생명주기) | Task 1, 2 완료 후 |

---

## 테스트 요구사항

### EditMode Test

- RoomClient 상태 전이 검증 (상태 머신 로직을 순수 메서드로 분리 시)
- Envelope 생성/파싱 검증 (각 메시지 타입별)

### PlayMode Test

- RoomClient 생성 → 룸 서버 접속 → 방 생성 → 방 참가 → GameStart 수신
  - 룸 서버 실행 필수 (통합 테스트)
- RoomAuthState 싱글톤 생성/읽기 검증

---

## 검증 방법

1. RoomClient가 룸 서버(:8080)에 TCP 접속 성공
2. 방 생성 요청 → CreateRoomResponse 수신 확인 (로그)
3. 방 참가 → JoinRoomResponse 수신 확인
4. GameStart 수신 → RoomAuthState에 토큰 기록 확인
5. Heartbeat 전송 확인 (10초 간격, 룸 서버 로그)

## 완료 기준

- [ ] RoomClient가 룸 서버와 TCP 통신 가능
- [ ] 모든 Envelope 메시지 타입 송수신 처리
- [ ] 상태 머신 전이 동작 확인
- [ ] RoomAuthState 싱글톤으로 토큰 전달 확인
- [ ] DontDestroyOnLoad 생명주기 정상 동작
- [ ] 컴파일 성공
