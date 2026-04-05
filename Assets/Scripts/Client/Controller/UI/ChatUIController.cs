using System.Collections.Generic;
using System.Text;
using Sos.Chat;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client
{
    /// <summary>
    /// 채팅 UI 컨트롤러. 입력 필드와 메시지 표시를 관리한다.
    /// Enter 키로 채팅 입력 토글, ESC로 포커스 해제.
    /// ChatClient 연결 전에는 패널을 숨기고, 룸/인게임 간 위치·크기를 전환한다.
    /// </summary>
    public class ChatUIController : MonoBehaviour
    {
        // -- 상수 --

        const int MaxMessages = 50;

        // -- Inspector --

        [Header("Chat Client")]
        [SerializeField] ChatClient chatClient;

        [Header("UI References")]
        [SerializeField] GameObject chatPanel;
        [SerializeField] RectTransform chatPanelRect;
        [SerializeField] TMP_InputField inputField;
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] Transform messageContent;
        [SerializeField] GameObject messagePrefab;

        [Header("Panel Layout")]
        [SerializeField] Vector2 roomPosition = new Vector2(300, 200);
        [SerializeField] Vector2 roomSize = new Vector2(600, 400);
        [SerializeField] Vector2 inGamePosition = new Vector2(200, 356);
        [SerializeField] Vector2 inGameSize = new Vector2(400, 200);


        // -- 정적 프로퍼티 --

        /// <summary>
        /// 채팅 입력 필드가 활성화되어 있는지 여부.
        /// 다른 입력 시스템에서 이 값을 확인하여 키보드 입력을 차단한다.
        /// </summary>
        public static bool IsChatFocused { get; private set; }

        /// <summary>
        /// 채팅 포커스가 해제된 프레임에서 true.
        /// MonoBehaviour.Update()가 ECS보다 먼저 실행되므로, 같은 프레임에
        /// ECS가 ESC를 처리하는 충돌을 방지한다. 매 프레임 Update()에서 재계산.
        /// </summary>
        public static bool WasChatFocusedThisFrame { get; private set; }

        // -- 내부 상태 --

        readonly Queue<GameObject> messageQueue = new Queue<GameObject>();
        ChatChannel currentChannel = ChatChannel.ChannelLobby;

        static readonly string SystemColor = "#FFCC00";

        // -- 생명주기 --

        static ChatUIController instance;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (inputField != null)
            {
                inputField.richText = false;
                inputField.gameObject.SetActive(false);
            }

            SubscribeEvents();
            UpdateChatPanelVisibility();
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            IsChatFocused = false;
            WasChatFocusedThisFrame = false;
        }

        void Update()
        {
            bool wasFocused = IsChatFocused;
            HandleKeyboardInput();

            WasChatFocusedThisFrame = wasFocused && !IsChatFocused;
        }

        // -- 패널 표시/숨김 --

        void UpdateChatPanelVisibility()
        {
            if (chatPanel == null) return;

            bool isConnected = chatClient != null
                && (chatClient.State == ChatClientState.Lobby || chatClient.State == ChatClientState.InSession);

            chatPanel.SetActive(isConnected);
        }

        void UpdatePanelPosition()
        {
            if (chatPanelRect == null) return;

            bool isInSession = chatClient != null && chatClient.State == ChatClientState.InSession;
            chatPanelRect.anchoredPosition = isInSession ? inGamePosition : roomPosition;
            chatPanelRect.sizeDelta = isInSession ? inGameSize : roomSize;
        }

        void ClearMessages()
        {
            while (messageQueue.Count > 0)
            {
                var messageObject = messageQueue.Dequeue();
                if (messageObject != null) Destroy(messageObject);
            }
        }

        // -- 키보드 입력 --

        void HandleKeyboardInput()
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter) && !Input.GetKeyDown(KeyCode.Escape))
                return;

            if (Input.GetKeyDown(KeyCode.Escape) && IsChatFocused)
            {
                DeactivateInput();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (!IsChatFocused)
                {
                    ActivateInput();
                }
                else
                {
                    TrySendMessage();
                    DeactivateInput();
                }
            }
        }

        void ActivateInput()
        {
            if (inputField == null) return;
            if (chatPanel != null && !chatPanel.activeSelf) return;

            inputField.gameObject.SetActive(true);
            inputField.ActivateInputField();
            inputField.Select();
            IsChatFocused = true;
        }

        void DeactivateInput()
        {
            if (inputField == null) return;

            inputField.DeactivateInputField();
            inputField.gameObject.SetActive(false);
            IsChatFocused = false;
        }

        // -- 메시지 전송 --

        void TrySendMessage()
        {
            if (inputField == null || chatClient == null) return;

            string content = inputField.text;
            inputField.text = "";

            if (string.IsNullOrWhiteSpace(content))
                return;

            int byteCount = Encoding.UTF8.GetByteCount(content);
            if (byteCount > ChatClient.MaxMessageBytes)
            {
                AddSystemMessage("메시지가 너무 깁니다");
                return;
            }

            chatClient.SendMessage(currentChannel, content);
        }

        // -- 메시지 표시 --

        void AddChatMessage(ChatReceive message)
        {
            string safeName = SanitizeRichText(message.SenderName);
            string safeContent = SanitizeRichText(message.Content);
            AppendMessage($"{safeName}: {safeContent}");
        }

        void AddSystemMessage(string content)
        {
            string formatted = $"<color={SystemColor}>[SYSTEM] {content}</color>";
            AppendMessage(formatted);
        }

        void AddSystemMessage(Sos.Chat.SystemMessage message)
        {
            string safeContent = SanitizeRichText(message.Content);
            string formatted = $"<color={SystemColor}>{safeContent}</color>";
            AppendMessage(formatted);
        }

        void AppendMessage(string formattedText)
        {
            if (messageContent == null || messagePrefab == null) return;

            // 링버퍼 초과 시 oldest 제거
            while (messageQueue.Count >= MaxMessages)
            {
                var oldest = messageQueue.Dequeue();
                if (oldest != null) Destroy(oldest);
            }

            var messageObject = Instantiate(messagePrefab, messageContent);
            var textComponent = messageObject.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.richText = true;
                textComponent.text = formattedText;
            }

            messageQueue.Enqueue(messageObject);

            // 레이아웃 갱신 + 스크롤 (다음 프레임에 수행)
            StartCoroutine(ScrollToBottomNextFrame());
        }

        // -- 채널 자동 전환 --

        void UpdateCurrentChannel()
        {
            bool isInSession = chatClient != null && chatClient.State == ChatClientState.InSession;
            currentChannel = isInSession ? ChatChannel.ChannelAll : ChatChannel.ChannelLobby;
        }

        // -- 이벤트 구독 --

        void SubscribeEvents()
        {
            if (chatClient == null) return;

            chatClient.OnMessageReceived += HandleMessageReceived;
            chatClient.OnSystemMessage += HandleSystemMessage;
            chatClient.OnChatError += HandleChatError;
            chatClient.OnConnected += HandleConnected;
            chatClient.OnDisconnected += HandleDisconnected;
            chatClient.OnAuthResult += HandleAuthResult;
        }

        void UnsubscribeEvents()
        {
            if (chatClient == null) return;

            chatClient.OnMessageReceived -= HandleMessageReceived;
            chatClient.OnSystemMessage -= HandleSystemMessage;
            chatClient.OnChatError -= HandleChatError;
            chatClient.OnConnected -= HandleConnected;
            chatClient.OnDisconnected -= HandleDisconnected;
            chatClient.OnAuthResult -= HandleAuthResult;
        }

        // -- 이벤트 핸들러 --

        void HandleMessageReceived(ChatReceive message)
        {
            AddChatMessage(message);
        }

        void HandleSystemMessage(Sos.Chat.SystemMessage message)
        {
            AddSystemMessage(message);
        }

        void HandleChatError(ChatError error)
        {
            switch (error.Code)
            {
                case ChatError.Types.ChatErrorCode.RateLimited:
                    AddSystemMessage("메시지를 너무 빠르게 보내고 있습니다");
                    break;
                case ChatError.Types.ChatErrorCode.MessageTooLong:
                    AddSystemMessage("메시지가 너무 깁니다");
                    break;
                case ChatError.Types.ChatErrorCode.UserNotFound:
                    AddSystemMessage("대상을 찾을 수 없습니다");
                    break;
                default:
                    if (!string.IsNullOrEmpty(error.Message))
                        AddSystemMessage(error.Message);
                    break;
            }
        }

        void HandleConnected()
        {
            ClearMessages();
            UpdateChatPanelVisibility();
            UpdatePanelPosition();
        }

        void HandleDisconnected()
        {
            DeactivateInput();
            UpdateChatPanelVisibility();
        }

        void HandleAuthResult(ChatAuthResult result)
        {
            if (result.Success)
            {
                // 세션 진입 시 룸 채팅 이력 초기화
                if (chatClient.State == ChatClientState.InSession)
                    ClearMessages();

                UpdateChatPanelVisibility();
                UpdateCurrentChannel();
                UpdatePanelPosition();
            }
        }

        // -- 유틸리티 --

        System.Collections.IEnumerator ScrollToBottomNextFrame()
        {
            yield return null;

            // ContentSizeFitter 갱신 강제
            if (messageContent is RectTransform contentRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// TMP Rich Text 태그 인젝션 방지. &lt; 뒤에 zero-width space를 삽입하여 태그 파싱을 무효화한다.
        /// </summary>
        static string SanitizeRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("<", "<\u200B");
        }
    }
}
