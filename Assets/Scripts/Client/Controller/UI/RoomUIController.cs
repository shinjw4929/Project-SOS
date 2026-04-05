using Sos.Room;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client
{
    /// <summary>
    /// 룸 서버 연동 UI 컨트롤러.
    /// RoomClient 이벤트를 구독하여 방 목록, 대기실, 연결 상태를 표시한다.
    /// </summary>
    public class RoomUIController : MonoBehaviour
    {
        // -- Inspector 참조 --

        [Header("Room Client")]
        [SerializeField] RoomClient roomClient;

        [Header("Chat Client")]
        [SerializeField] ChatClient chatClient;

        [Header("Panels")]
        [SerializeField] GameObject titlePanel;
        [SerializeField] Button gameStartButton;
        [SerializeField] Button exitButton;
        [SerializeField] GameObject connectingPanel;
        [SerializeField] GameObject lobbyPanel;
        [SerializeField] GameObject roomPanel;
        [SerializeField] GameObject gameStartingPanel;
        [SerializeField] GameObject errorPanel;

        [Header("Lobby UI")]
        [SerializeField] Transform roomListContent;
        [SerializeField] GameObject roomListItemPrefab;
        [SerializeField] TMP_InputField roomNameInput;
        [SerializeField] TMP_InputField userNameInput;
        [SerializeField] Button createRoomButton;
        [SerializeField] Button refreshButton;

        [Header("Room UI")]
        [SerializeField] TextMeshProUGUI roomTitleText;
        [SerializeField] Transform userListContent;
        [SerializeField] GameObject userListItemPrefab;
        [SerializeField] Button readyButton;
        [SerializeField] Button leaveButton;
        [SerializeField] Button startGameButton;
        [SerializeField] TextMeshProUGUI readyButtonText;

        [Header("Error UI")]
        [SerializeField] TextMeshProUGUI errorMessageText;
        [SerializeField] Button retryButton;

        [Header("Connection")]
        [SerializeField] string roomServerHost = "127.0.0.1";
        [SerializeField] ushort roomServerPort = 8080;

        // -- 내부 상태 --

        string currentUserId;
        bool isReady;
        string lastConnectionHost;
        ushort lastConnectionPort;

        // -- 생명주기 --

        void Start()
        {
            currentUserId = System.Guid.NewGuid().ToString().Substring(0, 8);

            SubscribeEvents();
            BindButtons();
            ShowPanel(RoomClientState.Disconnected);

            // 게임 시뮬레이션 정지 — GameStart 수신 시 복원
            Time.timeScale = 0f;
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            UnbindButtons();
        }

        // -- 이벤트 구독 --

        void SubscribeEvents()
        {
            if (roomClient == null) return;

            roomClient.OnStateChanged += HandleStateChanged;
            roomClient.OnRoomListReceived += HandleRoomListReceived;
            roomClient.OnRoomCreated += HandleRoomCreated;
            roomClient.OnRoomJoined += HandleRoomJoined;
            roomClient.OnRoomUpdated += HandleRoomUpdated;
            roomClient.OnRejected += HandleRejected;
            roomClient.OnError += HandleError;
            roomClient.OnGameStartReceived += HandleGameStartReceived;
        }

        void UnsubscribeEvents()
        {
            if (roomClient == null) return;

            roomClient.OnStateChanged -= HandleStateChanged;
            roomClient.OnRoomListReceived -= HandleRoomListReceived;
            roomClient.OnRoomCreated -= HandleRoomCreated;
            roomClient.OnRoomJoined -= HandleRoomJoined;
            roomClient.OnRoomUpdated -= HandleRoomUpdated;
            roomClient.OnRejected -= HandleRejected;
            roomClient.OnError -= HandleError;
            roomClient.OnGameStartReceived -= HandleGameStartReceived;
        }

        // -- 버튼 바인딩 --

        void BindButtons()
        {
            gameStartButton?.onClick.AddListener(OnGameStartClicked);
            exitButton?.onClick.AddListener(OnExitClicked);
            createRoomButton?.onClick.AddListener(OnCreateRoomClicked);
            refreshButton?.onClick.AddListener(OnRefreshClicked);
            readyButton?.onClick.AddListener(OnReadyClicked);
            leaveButton?.onClick.AddListener(OnLeaveClicked);
            startGameButton?.onClick.AddListener(OnStartGameClicked);
            retryButton?.onClick.AddListener(OnRetryClicked);
        }

        void UnbindButtons()
        {
            gameStartButton?.onClick.RemoveListener(OnGameStartClicked);
            exitButton?.onClick.RemoveListener(OnExitClicked);
            createRoomButton?.onClick.RemoveListener(OnCreateRoomClicked);
            refreshButton?.onClick.RemoveListener(OnRefreshClicked);
            readyButton?.onClick.RemoveListener(OnReadyClicked);
            leaveButton?.onClick.RemoveListener(OnLeaveClicked);
            startGameButton?.onClick.RemoveListener(OnStartGameClicked);
            retryButton?.onClick.RemoveListener(OnRetryClicked);
        }

        // -- 패널 전환 --

        void ShowPanel(RoomClientState state)
        {
            titlePanel?.SetActive(state == RoomClientState.Disconnected);
            connectingPanel?.SetActive(state == RoomClientState.Connecting);
            lobbyPanel?.SetActive(state == RoomClientState.Lobby);
            roomPanel?.SetActive(state == RoomClientState.InRoom);
            gameStartingPanel?.SetActive(state == RoomClientState.Matched);
            errorPanel?.SetActive(false);

            if (state == RoomClientState.InGame)
                gameObject.SetActive(false);
        }

        void ShowError(string message)
        {
            if (errorPanel != null)
                errorPanel.SetActive(true);

            if (errorMessageText != null)
                errorMessageText.text = message;
        }

        // -- 이벤트 핸들러 --

        System.Collections.IEnumerator DelayedRoomListRequest(float delaySec)
        {
            yield return new UnityEngine.WaitForSecondsRealtime(delaySec);
            if (roomClient != null && roomClient.State == RoomClientState.Lobby)
                roomClient.SendRoomListRequest();
        }

        void HandleStateChanged(RoomClientState state)
        {
            ShowPanel(state);

            if (state == RoomClientState.Lobby)
            {
                StartCoroutine(DelayedRoomListRequest(0.5f));

                // 방에서 나와 로비로 돌아오면 채팅 연결 해제 (패널 숨김)
                if (chatClient != null && chatClient.State != ChatClientState.Disconnected)
                    chatClient.Disconnect();
            }
        }

        void ConnectChatIfNeeded()
        {
            if (chatClient == null || chatClient.State != ChatClientState.Disconnected)
                return;

            string userName = userNameInput != null ? userNameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(userName))
                userName = currentUserId;

            chatClient.ConnectToChatServer(currentUserId, userName);
        }

        void HandleRoomListReceived(RoomListResponse response)
        {
            PopulateRoomList(response);
        }

        void HandleRoomCreated(RoomInfo room)
        {
            isReady = false;
            UpdateReadyButtonText();
            PopulateUserList(room);
            ConnectChatIfNeeded();
        }

        void HandleRoomJoined(RoomInfo room)
        {
            isReady = false;
            UpdateReadyButtonText();
            PopulateUserList(room);
            ConnectChatIfNeeded();
        }

        void HandleRoomUpdated(RoomInfo room)
        {
            PopulateUserList(room);
        }

        void HandleRejected(RejectResponse response)
        {
            // RoomClosed는 RoomClient가 Lobby로 전환하므로 에러 패널 불필요
            if (response.Reason == RejectResponse.Types.RejectReason.RoomClosed)
                return;

            string message = GetRejectMessage(response.Reason);
            ShowError(message);
        }

        void HandleError(string errorMessage)
        {
            ShowError(errorMessage);
        }

        void HandleGameStartReceived(GameStart gameStart)
        {
            // 게임 시뮬레이션 시작
            Time.timeScale = 1f;
        }

        // -- 버튼 핸들러 --

        void OnCreateRoomClicked()
        {
            string roomName = roomNameInput != null ? roomNameInput.text : "";
            string userName = userNameInput != null ? userNameInput.text : "";

            if (string.IsNullOrWhiteSpace(roomName))
            {
                ShowError("Please enter a room name");
                return;
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                ShowError("Please enter a nickname");
                return;
            }

            roomClient.SendCreateRoom(currentUserId, userName.Trim(), roomName.Trim(), 8);
        }

        void OnRefreshClicked()
        {
            roomClient.SendRoomListRequest();
        }

        void OnReadyClicked()
        {
            roomClient.SendToggleReady();
            isReady = !isReady;
            UpdateReadyButtonText();
        }

        void OnLeaveClicked()
        {
            roomClient.SendLeaveRoom();
            isReady = false;
            UpdateReadyButtonText();
        }

        void OnStartGameClicked()
        {
            roomClient.SendStartGame();
        }

        void OnGameStartClicked()
        {
            if (roomClient != null)
            {
                lastConnectionHost = roomServerHost;
                lastConnectionPort = roomServerPort;
                roomClient.ConnectToRoomServer(roomServerHost, roomServerPort);
            }
        }

        void OnExitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnRetryClicked()
        {
            errorPanel?.SetActive(false);

            // 이미 연결된 상태면 에러 패널만 닫음
            if (roomClient.State == RoomClientState.Lobby || roomClient.State == RoomClientState.InRoom)
                return;

            if (!string.IsNullOrEmpty(lastConnectionHost) && lastConnectionPort > 0)
                roomClient.ConnectToRoomServer(lastConnectionHost, lastConnectionPort);
        }

        // -- 방 목록 갱신 --

        void PopulateRoomList(RoomListResponse response)
        {
            if (roomListContent == null || roomListItemPrefab == null) return;

            ClearChildren(roomListContent);

            foreach (var room in response.Rooms)
            {
                var itemObject = Instantiate(roomListItemPrefab, roomListContent);

                // 방 이름 텍스트 설정
                var nameText = itemObject.transform.Find("RoomNameText");
                if (nameText != null)
                {
                    var textComponent = nameText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                        textComponent.text = room.RoomName;
                }

                // 인원 수 텍스트 설정
                var countText = itemObject.transform.Find("UserCountText");
                if (countText != null)
                {
                    var textComponent = countText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                        textComponent.text = $"{room.CurrentPlayers}/{room.MaxPlayers}";
                }

                // 호스트 이름 텍스트 설정
                var hostText = itemObject.transform.Find("HostNameText");
                if (hostText != null)
                {
                    var textComponent = hostText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                        textComponent.text = room.HostName;
                }

                // 입장 버튼 설정
                var joinButton = itemObject.GetComponentInChildren<Button>();
                if (joinButton != null)
                {
                    string roomId = room.RoomId;
                    joinButton.onClick.AddListener(() => OnJoinRoomClicked(roomId));
                }
            }
        }

        void OnJoinRoomClicked(string roomId)
        {
            string userName = userNameInput != null ? userNameInput.text : "";

            if (string.IsNullOrWhiteSpace(userName))
            {
                ShowError("Please enter a nickname");
                return;
            }

            roomClient.SendJoinRoom(currentUserId, userName.Trim(), roomId);
        }

        // -- 유저 목록 갱신 --

        void PopulateUserList(RoomInfo room)
        {
            if (roomTitleText != null)
                roomTitleText.text = $"{room.RoomName} ({room.Players.Count}/{room.MaxPlayers})";

            if (userListContent == null || userListItemPrefab == null) return;

            ClearChildren(userListContent);

            foreach (var userInfo in room.Players)
            {
                var itemObject = Instantiate(userListItemPrefab, userListContent);

                // 유저 이름 텍스트 설정
                var nameText = itemObject.transform.Find("UserNameText");
                if (nameText != null)
                {
                    var textComponent = nameText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                    {
                        string displayName = userInfo.PlayerName;
                        if (userInfo.IsHost)
                            displayName += " [HOST]";
                        textComponent.text = displayName;
                    }
                }

                // 준비 상태 텍스트 설정
                var statusText = itemObject.transform.Find("StatusText");
                if (statusText != null)
                {
                    var textComponent = statusText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                        textComponent.text = userInfo.IsReady ? "READY" : "";
                }
            }

            bool isHost = room.HostId == currentUserId;
            startGameButton?.gameObject.SetActive(isHost);
            readyButton?.gameObject.SetActive(!isHost);
        }

        // -- 유틸리티 --

        void UpdateReadyButtonText()
        {
            if (readyButtonText != null)
                readyButtonText.text = isReady ? "Cancel Ready" : "Ready";
        }

        static void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
                Destroy(parent.GetChild(index).gameObject);
        }

        static string GetRejectMessage(RejectResponse.Types.RejectReason reason) => reason switch
        {
            RejectResponse.Types.RejectReason.RoomFull => "Room is full",
            RejectResponse.Types.RejectReason.RoomNotFound => "Room not found",
            RejectResponse.Types.RejectReason.NotHost => "Only the host can start the game",
            RejectResponse.Types.RejectReason.NotAllReady => "Not all players are ready",
            RejectResponse.Types.RejectReason.RateLimited => "Too many requests",
            RejectResponse.Types.RejectReason.DuplicatePlayer => "Already connected",
            RejectResponse.Types.RejectReason.AlreadyInRoom => "Already in a room",
            RejectResponse.Types.RejectReason.InvalidRequest => "Invalid request",
            RejectResponse.Types.RejectReason.RoomClosed => "Room has been closed",
            _ => "Unknown error"
        };

        // -- 공개 API --

        /// <summary>
        /// 외부에서 접속 정보를 설정한다. 재접속 시 사용된다.
        /// </summary>
        public void SetConnectionInfo(string host, ushort port)
        {
            lastConnectionHost = host;
            lastConnectionPort = port;
        }

        /// <summary>
        /// 외부에서 유저 ID를 설정한다. 인증 시스템 연동 시 사용된다.
        /// </summary>
        public void SetUserId(string userId)
        {
            currentUserId = userId;
        }
    }
}
