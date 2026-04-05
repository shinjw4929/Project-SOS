using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sos.Chat;
using Shared;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Action = System.Action;

namespace Client
{
    public enum ChatClientState
    {
        Disconnected,
        Connecting,
        Lobby,
        InSession
    }

    /// <summary>
    /// 채팅 서버와의 TCP 통신을 담당하는 MonoBehaviour 클라이언트.
    /// RoomClient와 동일한 length-prefix(4byte LE) 프레이밍 + Protobuf ChatEnvelope 패턴.
    /// </summary>
    public class ChatClient : MonoBehaviour
    {
        // -- 상수 --

        const int MaxRetryCount = 3;
        const float InitialRetryDelay = 1.0f;
        const float HeartbeatIntervalSeconds = 10.0f;
        const int InitialBufferSize = 8192;
        public const int MaxMessageBytes = 200;

        // -- Inspector --

        [Header("Chat Server")]
        [SerializeField] string chatServerHost = "127.0.0.1";
        [SerializeField] ushort chatServerPort = 8082;

        // -- 상태 --

        ChatClientState state = ChatClientState.Disconnected;
        string userId;
        string userName;
        string pendingSessionId;
        bool sessionReauthDone;

        // EntityQuery 캐싱 (세션 재인증 폴링용)
        World cachedWorld;
        EntityQuery cachedAuthQuery;

        public ChatClientState State
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

        // -- TCP --

        TcpClient tcpClient;
        NetworkStream networkStream;
        CancellationTokenSource cancellationTokenSource;

        byte[] receiveBuffer = new byte[InitialBufferSize];
        int receiveBufferLength;

        Coroutine heartbeatCoroutine;
        Coroutine sessionReauthCoroutine;
        Coroutine reconnectCoroutine;

        // -- 이벤트 --

        public event Action<ChatAuthResult> OnAuthResult;
        public event Action<ChatReceive> OnMessageReceived;
        public event Action<Sos.Chat.SystemMessage> OnSystemMessage;
        public event Action<ChatError> OnChatError;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<ChatClientState> OnStateChanged;

        // -- 생명주기 --

        static ChatClient instance;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.quitting += Cleanup;
            SubscribeGameOverEvent();
        }

        void OnEnable()
        {
            // GameOverEvents.Clear() 호출 후 재구독 보장 (DontDestroyOnLoad 객체)
            SubscribeGameOverEvent();
        }

        void OnDestroy()
        {
            Application.quitting -= Cleanup;
            GameOverEvents.OnGameOver -= HandleGameOver;
            Cleanup();
        }

        void SubscribeGameOverEvent()
        {
            GameOverEvents.OnGameOver -= HandleGameOver;
            GameOverEvents.OnGameOver += HandleGameOver;
        }

        void Cleanup()
        {
            Disconnect();
        }

        // -- 공개 연결 API --

        /// <summary>
        /// 채팅 서버에 TCP 연결 후 로비 인증을 수행한다.
        /// </summary>
        public async void ConnectToChatServer(string userId, string userName)
        {
            if (State != ChatClientState.Disconnected)
            {
                LogWarning("Already connected or connecting. Disconnect first.");
                return;
            }

            this.userId = userId;
            this.userName = userName;
            pendingSessionId = null;
            sessionReauthDone = false;

            State = ChatClientState.Connecting;
            cancellationTokenSource = new CancellationTokenSource();

            for (int attempt = 0; attempt < MaxRetryCount; attempt++)
            {
                try
                {
                    tcpClient = new TcpClient();
                    LogInfo($"Connecting to chat server {chatServerHost}:{chatServerPort} (attempt {attempt + 1}/{MaxRetryCount})");

                    await tcpClient.ConnectAsync(chatServerHost, chatServerPort);

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        CloseTcpClient();
                        return;
                    }

                    networkStream = tcpClient.GetStream();
                    LogInfo("Connected to chat server");
                    OnConnected?.Invoke();

                    StartHeartbeat();
                    _ = ReceiveLoopAsync(cancellationTokenSource.Token);

                    // 로비 인증 자동 전송
                    SendAuth(userId, userName, "");
                    return;
                }
                catch (Exception exception)
                {
                    LogWarning($"Connection attempt {attempt + 1} failed: {exception.Message}");
                    CloseTcpClient();

                    if (attempt < MaxRetryCount - 1 && !cancellationTokenSource.IsCancellationRequested)
                    {
                        int delayMs = (int)(InitialRetryDelay * 1000 * (1 << attempt));
                        try
                        {
                            await Task.Delay(delayMs, cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            State = ChatClientState.Disconnected;
                            return;
                        }
                    }
                }
            }

            State = ChatClientState.Disconnected;
            OnChatError?.Invoke(new ChatError
            {
                Code = ChatError.Types.ChatErrorCode.Unknown,
                Message = "Failed to connect to chat server after multiple attempts"
            });
        }

        /// <summary>
        /// TCP 연결을 종료하고 상태를 Disconnected로 초기화한다.
        /// </summary>
        public void Disconnect()
        {
            StopHeartbeat();
            StopSessionReauthPolling();
            StopReconnect();

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            CloseTcpClient();
            receiveBufferLength = 0;

            var previousState = State;
            State = ChatClientState.Disconnected;

            if (previousState != ChatClientState.Disconnected)
                OnDisconnected?.Invoke();
        }

        // -- 공개 전송 API --

        /// <summary>
        /// 채팅 메시지를 전송한다. 클라이언트 사전 검증(빈 문자열, 200bytes 초과)을 수행한다.
        /// </summary>
        public void SendMessage(ChatChannel channel, string content)
        {
            if (State != ChatClientState.Lobby && State != ChatClientState.InSession)
            {
                LogWarning("Cannot send message: not authenticated");
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                LogWarning("Cannot send empty message");
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(content);
            if (byteCount > MaxMessageBytes)
            {
                LogWarning($"Message too long: {byteCount} bytes (max: {MaxMessageBytes})");
                return;
            }

            var envelope = new ChatEnvelope
            {
                Send = new ChatSend
                {
                    Channel = channel,
                    Content = content
                }
            };
            SendEnvelope(envelope);
        }

        // -- 인증 --

        void SendAuth(string userId, string userName, string sessionId)
        {
            var envelope = new ChatEnvelope
            {
                Auth = new ChatAuth
                {
                    UserId = userId,
                    UserName = userName,
                    SessionId = sessionId ?? ""
                }
            };
            SendEnvelope(envelope);
        }

        // -- 세션 재인증 트리거 --

        /// <summary>
        /// Lobby 상태에서 세션 진입을 감지하여 재인증을 수행하는 폴링을 시작한다.
        /// RoomAuthState.SessionId 확정을 감지한다.
        /// </summary>
        void StartSessionReauthPolling()
        {
            StopSessionReauthPolling();
            sessionReauthDone = false;
            sessionReauthCoroutine = StartCoroutine(SessionReauthPollingLoop());
        }

        void StopSessionReauthPolling()
        {
            if (sessionReauthCoroutine != null)
            {
                StopCoroutine(sessionReauthCoroutine);
                sessionReauthCoroutine = null;
            }
        }

        System.Collections.IEnumerator SessionReauthPollingLoop()
        {
            var interval = new WaitForSecondsRealtime(1.0f);

            while (!sessionReauthDone)
            {
                yield return interval;

                if (State != ChatClientState.Lobby)
                    continue;

                if (TryGetSessionCredentials(out string sessionId))
                {
                    LogInfo($"Session detected: sessionId={sessionId}. Re-authenticating.");
                    pendingSessionId = sessionId;
                    SendAuth(userId, userName, sessionId);
                    sessionReauthDone = true;
                }
            }
        }

        /// <summary>
        /// ECS에서 RoomAuthState.SessionId를 읽어온다.
        /// SessionId가 확정되면 true를 반환한다.
        /// </summary>
        bool TryGetSessionCredentials(out string sessionId)
        {
            sessionId = null;

            var world = FindClientWorld();
            if (world == null || !world.IsCreated)
                return false;

            // World가 바뀌면 캐싱된 Query 재생성
            if (cachedWorld != world)
            {
                cachedWorld = world;
                cachedAuthQuery = world.EntityManager.CreateEntityQuery(typeof(RoomAuthState));
            }

            if (!cachedAuthQuery.TryGetSingleton<RoomAuthState>(out var authState) || authState.SessionId.Length == 0)
                return false;

            sessionId = authState.SessionId.ToString();
            return true;
        }

        // -- GameOver 처리 --

        void HandleGameOver()
        {
            if (State == ChatClientState.Disconnected)
                return;

            LogInfo("GameOver detected. Disconnecting chat.");
            Disconnect();
        }

        // -- 전송 내부 --

        async void SendEnvelope(ChatEnvelope envelope)
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

        // -- 수신 루프 --

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
                        LogInfo("Chat server closed connection");
                        HandleDisconnection();
                        return;
                    }

                    receiveBufferLength += bytesRead;

                    // 버퍼에서 완전한 메시지를 모두 추출
                    int offset = 0;
                    while (ProtobufFraming.TryDeframe(receiveBuffer, ref offset, receiveBufferLength, ChatEnvelope.Parser, out var envelope))
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

        // -- 메시지 디스패치 --

        void DispatchEnvelope(ChatEnvelope envelope)
        {
            switch (envelope.PayloadCase)
            {
                case ChatEnvelope.PayloadOneofCase.AuthResult:
                    HandleAuthResult(envelope.AuthResult);
                    break;

                case ChatEnvelope.PayloadOneofCase.Receive:
                    OnMessageReceived?.Invoke(envelope.Receive);
                    break;

                case ChatEnvelope.PayloadOneofCase.SystemMessage:
                    OnSystemMessage?.Invoke(envelope.SystemMessage);
                    break;

                case ChatEnvelope.PayloadOneofCase.Error:
                    HandleError(envelope.Error);
                    break;

                case ChatEnvelope.PayloadOneofCase.Heartbeat:
                    // 서버 하트비트 응답 — 별도 처리 불필요
                    break;

                default:
                    LogWarning($"Unhandled payload case: {envelope.PayloadCase}");
                    break;
            }
        }

        void HandleAuthResult(ChatAuthResult result)
        {
            if (result.Success)
            {
                if (!string.IsNullOrEmpty(pendingSessionId))
                {
                    State = ChatClientState.InSession;
                    StopSessionReauthPolling();
                }
                else
                {
                    State = ChatClientState.Lobby;
                    StartSessionReauthPolling();
                }

                LogInfo($"Chat authenticated: {State}");
            }
            else
            {
                LogWarning($"Chat auth failed: {result.Reason}");
            }

            OnAuthResult?.Invoke(result);
        }

        void HandleError(ChatError error)
        {
            LogWarning($"Chat error [{error.Code}]: {error.Message}");
            OnChatError?.Invoke(error);
        }

        // -- 하트비트 --

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

                if (State == ChatClientState.Lobby || State == ChatClientState.InSession)
                {
                    SendEnvelope(new ChatEnvelope { Heartbeat = new ChatHeartbeat() });
                }
            }
        }

        // -- 연결 해제 처리 --

        void HandleDisconnection()
        {
            StopHeartbeat();
            StopSessionReauthPolling();

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }

            CloseTcpClient();
            receiveBufferLength = 0;

            var previousState = State;
            State = ChatClientState.Disconnected;

            if (previousState != ChatClientState.Disconnected)
                OnDisconnected?.Invoke();

            // 인증 완료 상태에서 예기치 않게 끊긴 경우 재연결 시도
            if (previousState == ChatClientState.Lobby || previousState == ChatClientState.InSession)
            {
                if (!string.IsNullOrEmpty(userId))
                {
                    LogInfo("Unexpected disconnect from chat server, will reconnect after delay");
                    reconnectCoroutine = StartCoroutine(DelayedReconnect(3f));
                }
            }
        }

        void StopReconnect()
        {
            if (reconnectCoroutine != null)
            {
                StopCoroutine(reconnectCoroutine);
                reconnectCoroutine = null;
            }
        }

        System.Collections.IEnumerator DelayedReconnect(float delaySec)
        {
            yield return new WaitForSecondsRealtime(delaySec);
            reconnectCoroutine = null;
            if (State == ChatClientState.Disconnected && !string.IsNullOrEmpty(userId))
                ConnectToChatServer(userId, userName);
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

        // -- ECS 브릿지 --

        World FindClientWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                    return world;
            }
            return null;
        }

        // -- 로깅 --

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

        static void LogStateChange(ChatClientState previous, ChatClientState next)
        {
            FixedString128Bytes fixedMessage = default;
            fixedMessage.Append((FixedString32Bytes)"Chat State: ");
            fixedMessage.Append(previous.ToString());
            fixedMessage.Append((FixedString32Bytes)" -> ");
            fixedMessage.Append(next.ToString());
            GameLogger.Info(LogWorld.Client, LogCategory.Network, in fixedMessage);
        }
    }
}
