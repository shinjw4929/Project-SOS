using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sos.Room;
using Shared;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Client
{
    public enum RoomClientState
    {
        Disconnected,
        Connecting,
        Lobby,
        InRoom,
        Matched,
        InGame
    }

    /// <summary>
    /// 룸 서버와의 TCP 통신을 담당하는 MonoBehaviour 클라이언트.
    /// length-prefix(4byte LE) 프레이밍을 사용하며, Protobuf Envelope으로 메시지를 교환한다.
    /// </summary>
    public class RoomClient : MonoBehaviour
    {
        // ── 상수 ──

        const int InitialBufferSize = 8192;
        const float HeartbeatIntervalSeconds = 10f;
        const int MaxRetryCount = 3;

        // ── 상태 ──

        RoomClientState state = RoomClientState.Disconnected;
        string roomServerHost;  // 재접속용 서버 주소
        ushort roomServerPort;

        public RoomClientState State
        {
            get => state;
            private set
            {
                if (state == value) return;
                var previous = state;
                state = value;
                LogStateChange(previous, value);
                OnStateChanged?.Invoke(value);
            }
        }

        // ── TCP ──

        TcpClient tcpClient;
        NetworkStream networkStream;
        CancellationTokenSource cancellationTokenSource;

        // 수신 버퍼
        byte[] receiveBuffer = new byte[InitialBufferSize];
        int receiveBufferLength; // 버퍼 내 유효 데이터 바이트 수

        // 하트비트
        Coroutine heartbeatCoroutine;

        // ── 이벤트 ──

        public event Action<RoomInfo> OnRoomCreated;
        public event Action<RoomInfo> OnRoomJoined;
        public event Action<RoomListResponse> OnRoomListReceived;
        public event Action<RoomInfo> OnRoomUpdated;
        public event Action<GameStart> OnGameStartReceived;
        public event Action<RejectResponse> OnRejected;
        public event Action<string> OnError;
        public event Action<RoomClientState> OnStateChanged;

        // ── 생명주기 ──

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.quitting += Cleanup;
        }

        void OnDestroy()
        {
            Application.quitting -= Cleanup;
            Cleanup();
        }

        void Cleanup()
        {
            Disconnect();
        }

        // ── 공개 연결 API ──

        /// <summary>
        /// 룸 서버에 TCP 연결을 시도한다. 최대 3회 지수 백오프(1s, 2s, 4s) 재시도 후
        /// 성공 시 Lobby 상태로 전환되며, 수신 루프와 하트비트가 시작된다.
        /// </summary>
        public async void ConnectToRoomServer(string host, ushort port)
        {
            if (State != RoomClientState.Disconnected)
            {
                LogWarning("Already connected or connecting. Disconnect first.");
                return;
            }

            roomServerHost = host;
            roomServerPort = port;
            State = RoomClientState.Connecting;
            cancellationTokenSource = new CancellationTokenSource();

            for (int attempt = 0; attempt < MaxRetryCount; attempt++)
            {
                try
                {
                    tcpClient = new TcpClient();
                    LogInfo($"Connecting to room server {host}:{port} (attempt {attempt + 1}/{MaxRetryCount})");

                    await tcpClient.ConnectAsync(host, port);

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        CloseTcpClient();
                        return;
                    }

                    networkStream = tcpClient.GetStream();
                    State = RoomClientState.Lobby;
                    LogInfo("Connected to room server");

                    StartHeartbeat();
                    _ = ReceiveLoopAsync(cancellationTokenSource.Token);
                    return; // 연결 성공
                }
                catch (Exception exception)
                {
                    LogWarning($"Connection attempt {attempt + 1} failed: {exception.Message}");
                    CloseTcpClient();

                    if (attempt < MaxRetryCount - 1 && !cancellationTokenSource.IsCancellationRequested)
                    {
                        int delayMs = 1000 * (1 << attempt); // 1s, 2s, 4s
                        try
                        {
                            await Task.Delay(delayMs, cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // 대기 중 취소됨
                            State = RoomClientState.Disconnected;
                            return;
                        }
                    }
                }
            }

            // 모든 재시도 실패
            State = RoomClientState.Disconnected;
            OnError?.Invoke("Failed to connect to room server after multiple attempts");
        }

        /// <summary>
        /// TCP 연결을 종료하고 상태를 Disconnected로 초기화한다.
        /// </summary>
        public void Disconnect()
        {
            StopHeartbeat();

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            CloseTcpClient();
            receiveBufferLength = 0;
            State = RoomClientState.Disconnected;
        }

        /// <summary>
        /// TCP 연결을 끊고 Disconnected 상태로 복귀한 뒤, RoomAuthState ECS 싱글톤을 초기화하고
        /// 룸 서버에 재접속한다.
        /// </summary>
        public void ReturnToLobby()
        {
            Disconnect();
            ClearRoomAuthState();

            if (!string.IsNullOrEmpty(roomServerHost))
                ConnectToRoomServer(roomServerHost, roomServerPort);
        }

        /// <summary>
        /// 게임 종료 후 로비로 복귀한다. Netcode 연결 해제 후 룸 서버에 재접속한다.
        /// </summary>
        public void OnGameOver()
        {
            // Netcode 연결은 이미 끊어진 상태이거나 외부에서 끊어야 함
            ClearRoomAuthState();

            if (!string.IsNullOrEmpty(roomServerHost))
            {
                State = RoomClientState.Disconnected;
                ConnectToRoomServer(roomServerHost, roomServerPort);
            }
            else
            {
                State = RoomClientState.Disconnected;
                OnError?.Invoke("Cannot return to lobby: room server address unknown");
            }
        }

        // ── 공개 전송 API ──

        public void SendCreateRoom(string userId, string userName, string roomName, uint maxPlayers)
        {
            var envelope = new Envelope
            {
                CreateRoom = new CreateRoomRequest
                {
                    PlayerId = userId,
                    PlayerName = userName,
                    RoomName = roomName,
                    MaxPlayers = maxPlayers
                }
            };
            SendEnvelope(envelope);
        }

        public void SendJoinRoom(string userId, string userName, string roomId)
        {
            var envelope = new Envelope
            {
                JoinRoom = new JoinRoomRequest
                {
                    PlayerId = userId,
                    PlayerName = userName,
                    RoomId = roomId
                }
            };
            SendEnvelope(envelope);
        }

        public void SendLeaveRoom()
        {
            var envelope = new Envelope
            {
                LeaveRoom = new LeaveRoomRequest()
            };
            SendEnvelope(envelope);
            State = RoomClientState.Lobby;
        }

        public void SendToggleReady()
        {
            var envelope = new Envelope
            {
                ToggleReady = new ToggleReadyRequest()
            };
            SendEnvelope(envelope);
        }

        /// <summary>호스트 전용: 게임 시작 요청</summary>
        public void SendStartGame()
        {
            var envelope = new Envelope
            {
                StartGame = new StartGameRequest()
            };
            SendEnvelope(envelope);
        }

        public void SendRoomListRequest(uint page = 0, uint pageSize = 20)
        {
            var envelope = new Envelope
            {
                RoomListRequest = new RoomListRequest
                {
                    Page = page,
                    PageSize = pageSize
                }
            };
            SendEnvelope(envelope);
        }

        // ── 전송 내부 ──

        async void SendEnvelope(Envelope envelope)
        {
            if (networkStream == null || !networkStream.CanWrite)
            {
                LogWarning("Cannot send: stream is not writable");
                return;
            }

            try
            {
                byte[] framedData = ProtobufFraming.Frame(envelope);
                await networkStream.WriteAsync(framedData, 0, framedData.Length);
            }
            catch (Exception exception)
            {
                LogError($"Send failed: {exception.Message}");
                HandleDisconnection();
            }
        }

        // ── 수신 루프 ──

        async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && networkStream != null)
                {
                    // 버퍼 공간이 부족하면 확장
                    if (receiveBufferLength == receiveBuffer.Length)
                    {
                        int newSize = receiveBuffer.Length * 2;
                        if (newSize > ProtobufFraming.MaxMessageSize + ProtobufFraming.HeaderSize)
                            newSize = ProtobufFraming.MaxMessageSize + ProtobufFraming.HeaderSize;

                        if (receiveBufferLength >= newSize)
                        {
                            LogError("Receive buffer overflow");
                            HandleDisconnection();
                            return;
                        }

                        var newBuffer = new byte[newSize];
                        Buffer.BlockCopy(receiveBuffer, 0, newBuffer, 0, receiveBufferLength);
                        receiveBuffer = newBuffer;
                    }

                    int bytesRead = await networkStream.ReadAsync(
                        receiveBuffer,
                        receiveBufferLength,
                        receiveBuffer.Length - receiveBufferLength,
                        cancellationToken);

                    if (bytesRead == 0)
                    {
                        LogInfo("Server closed connection");
                        HandleDisconnection();
                        return;
                    }

                    receiveBufferLength += bytesRead;

                    // 버퍼에서 완전한 메시지를 모두 추출
                    int offset = 0;
                    while (ProtobufFraming.TryDeframe(receiveBuffer, ref offset, receiveBufferLength, out var envelope))
                    {
                        DispatchEnvelope(envelope);
                    }

                    // 남은 데이터를 버퍼 앞쪽으로 이동 (compact)
                    if (offset > 0)
                    {
                        int remaining = receiveBufferLength - offset;
                        if (remaining > 0)
                            Buffer.BlockCopy(receiveBuffer, offset, receiveBuffer, 0, remaining);
                        receiveBufferLength = remaining;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 종료
            }
            catch (Exception exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    LogError($"Receive error: {exception.Message}");
                    HandleDisconnection();
                }
            }
        }

        // ── 메시지 디스패치 ──

        void DispatchEnvelope(Envelope envelope)
        {
            switch (envelope.PayloadCase)
            {
                case Envelope.PayloadOneofCase.CreateRoomResponse:
                    HandleCreateRoomResponse(envelope.CreateRoomResponse);
                    break;

                case Envelope.PayloadOneofCase.JoinRoomResponse:
                    HandleJoinRoomResponse(envelope.JoinRoomResponse);
                    break;

                case Envelope.PayloadOneofCase.RoomListResponse:
                    OnRoomListReceived?.Invoke(envelope.RoomListResponse);
                    break;

                case Envelope.PayloadOneofCase.RoomUpdate:
                    OnRoomUpdated?.Invoke(envelope.RoomUpdate.Room);
                    break;

                case Envelope.PayloadOneofCase.GameStart:
                    HandleGameStart(envelope.GameStart);
                    break;

                case Envelope.PayloadOneofCase.Reject:
                    OnRejected?.Invoke(envelope.Reject);
                    break;

                case Envelope.PayloadOneofCase.Heartbeat:
                    // 서버 하트비트 응답 - 별도 처리 불필요
                    break;

                default:
                    LogWarning($"Unhandled payload case: {envelope.PayloadCase}");
                    break;
            }
        }

        void HandleCreateRoomResponse(CreateRoomResponse response)
        {
            if (response.Success)
            {
                State = RoomClientState.InRoom;
                OnRoomCreated?.Invoke(response.Room);
            }
            else
            {
                OnError?.Invoke($"CreateRoom failed: {response.Reason}");
            }
        }

        void HandleJoinRoomResponse(JoinRoomResponse response)
        {
            if (response.Success)
            {
                State = RoomClientState.InRoom;
                OnRoomJoined?.Invoke(response.Room);
            }
            else
            {
                OnError?.Invoke($"JoinRoom failed: {response.Reason}");
            }
        }

        void HandleGameStart(GameStart gameStart)
        {
            LogInfo($"GameStart received: host={gameStart.GameServerHost}, port={gameStart.GameServerPort}");

            State = RoomClientState.Matched;

            WriteRoomAuthState(gameStart.AuthToken, gameStart.SessionId);

            // AutoConnectPort=7979로 이미 Netcode 연결이 자동 수립됨
            // RoomAuthState에 토큰을 기록하면 GoInGameClientSystem이 자동으로 RPC 전송
            State = RoomClientState.InGame;

            OnGameStartReceived?.Invoke(gameStart);
        }

        // ── ECS 브릿지 ──

        World FindClientWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                    return world;
            }
            return null;
        }

        void WriteRoomAuthState(string authToken, string sessionId)
        {
            var world = FindClientWorld();
            if (world == null) return;

            if (authToken.Length > 125 || sessionId.Length > 125)
            {
                LogError("Auth token or session ID exceeds FixedString128Bytes capacity");
                return;
            }

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(typeof(RoomAuthState));
            if (query.TryGetSingletonEntity<RoomAuthState>(out var entity))
            {
                entityManager.SetComponentData(entity, new RoomAuthState
                {
                    AuthToken = new FixedString128Bytes(authToken),
                    SessionId = new FixedString128Bytes(sessionId)
                });
                LogInfo("RoomAuthState updated with game session credentials");
            }
            else
            {
                LogWarning("RoomAuthState singleton not found");
            }
        }

        void ClearRoomAuthState()
        {
            var world = FindClientWorld();
            if (world == null) return;

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(typeof(RoomAuthState));
            if (query.TryGetSingletonEntity<RoomAuthState>(out var entity))
            {
                entityManager.SetComponentData(entity, new RoomAuthState
                {
                    AuthToken = default,
                    SessionId = default
                });
            }
        }

        // ── 하트비트 ──

        void StartHeartbeat()
        {
            StopHeartbeat();
            heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
        }

        void StopHeartbeat()
        {
            if (heartbeatCoroutine != null)
            {
                StopCoroutine(heartbeatCoroutine);
                heartbeatCoroutine = null;
            }
        }

        System.Collections.IEnumerator HeartbeatLoop()
        {
            var interval = new WaitForSecondsRealtime(HeartbeatIntervalSeconds);

            while (true)
            {
                yield return interval;

                if (State == RoomClientState.Lobby || State == RoomClientState.InRoom)
                {
                    SendEnvelope(new Envelope { Heartbeat = new RoomHeartbeat() });
                }
            }
        }

        // ── 연결 해제 처리 ──

        System.Collections.IEnumerator DelayedReconnect(float delaySec)
        {
            yield return new WaitForSecondsRealtime(delaySec);
            if (State == RoomClientState.Disconnected && !string.IsNullOrEmpty(roomServerHost))
                ConnectToRoomServer(roomServerHost, roomServerPort);
        }

        void HandleDisconnection()
        {
            StopHeartbeat();

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            CloseTcpClient();
            receiveBufferLength = 0;

            var previousState = State;
            State = RoomClientState.Disconnected;

            // 로비/방 상태에서 예기치 않게 끊긴 경우 지연 후 재접속 시도
            if (previousState == RoomClientState.Lobby || previousState == RoomClientState.InRoom)
            {
                LogInfo("Unexpected disconnect, will reconnect after delay");
                if (!string.IsNullOrEmpty(roomServerHost))
                {
                    StartCoroutine(DelayedReconnect(3f));
                    return;
                }
            }

            if (previousState != RoomClientState.Disconnected)
                OnError?.Invoke("Disconnected from room server");
        }

        void CloseTcpClient()
        {
            if (networkStream != null)
            {
                try { networkStream.Close(); } catch { /* 무시 */ }
                networkStream = null;
            }

            if (tcpClient != null)
            {
                try { tcpClient.Close(); } catch { /* 무시 */ }
                tcpClient = null;
            }
        }

        // ── 로깅 ──

        static void LogInfo(string message)
        {
            FixedString128Bytes fixedMessage = message.Length > 120 ? message.Substring(0, 120) : message;
            GameLogger.Info(LogWorld.Client, LogCategory.Network, in fixedMessage);
        }

        static void LogWarning(string message)
        {
            FixedString128Bytes fixedMessage = message.Length > 120 ? message.Substring(0, 120) : message;
            GameLogger.Warning(LogWorld.Client, LogCategory.Network, in fixedMessage);
        }

        static void LogError(string message)
        {
            FixedString128Bytes fixedMessage = message.Length > 120 ? message.Substring(0, 120) : message;
            GameLogger.Error(LogWorld.Client, LogCategory.Network, in fixedMessage);
        }

        static void LogStateChange(RoomClientState previous, RoomClientState next)
        {
            FixedString128Bytes fixedMessage = default;
            fixedMessage.Append((FixedString32Bytes)"State: ");
            fixedMessage.Append(previous.ToString());
            fixedMessage.Append((FixedString32Bytes)" -> ");
            fixedMessage.Append(next.ToString());
            GameLogger.Info(LogWorld.Client, LogCategory.Network, in fixedMessage);
        }
    }
}
