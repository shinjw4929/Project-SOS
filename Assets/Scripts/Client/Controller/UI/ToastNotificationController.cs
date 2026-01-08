using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Shared;
using TMPro;

namespace Client
{
    /// <summary>
    /// 토스트 알림 UI 컨트롤러
    /// NotificationState 싱글톤을 읽어 화면에 토스트 메시지 표시
    /// </summary>
    public class ToastNotificationController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private CanvasGroup toastCanvasGroup;
        [SerializeField] private TextMeshProUGUI toastText;

        [Header("Settings")]
        [SerializeField] private float displayDuration = 2f;
        [SerializeField] private float fadeDuration = 0.3f;

        private World _clientWorld;
        private EntityQuery _notificationStateQuery;

        private float _displayTimer;
        private bool _isShowing;

        private void Start()
        {
            if (toastCanvasGroup != null)
            {
                toastCanvasGroup.alpha = 0f;
            }
        }

        private void Update()
        {
            // 1. 월드 초기화
            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                InitializeWorld();
                if (_clientWorld == null) return;
            }

            // 2. 알림 상태 확인
            CheckNotificationState();

            // 3. 표시 타이머 처리
            UpdateToastDisplay();
        }

        private void InitializeWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    _clientWorld = world;
                    _notificationStateQuery = world.EntityManager.CreateEntityQuery(typeof(NotificationState));
                    break;
                }
            }
        }

        private void CheckNotificationState()
        {
            if (_notificationStateQuery == null || _notificationStateQuery.IsEmpty) return;

            var notificationState = _notificationStateQuery.GetSingleton<NotificationState>();

            if (notificationState.PendingNotification != NotificationType.None)
            {
                ShowToast(notificationState.PendingNotification);

                // 싱글톤 리셋
                _notificationStateQuery.SetSingleton(new NotificationState
                {
                    PendingNotification = NotificationType.None
                });
            }
        }

        private void ShowToast(NotificationType type)
        {
            string message = GetMessageForType(type);

            if (toastText != null)
            {
                toastText.text = message;
            }

            if (toastCanvasGroup != null)
            {
                toastCanvasGroup.alpha = 1f;
            }

            _displayTimer = displayDuration;
            _isShowing = true;
        }

        private void UpdateToastDisplay()
        {
            if (!_isShowing) return;

            _displayTimer -= Time.deltaTime;

            if (_displayTimer <= 0f)
            {
                // 페이드아웃
                if (toastCanvasGroup != null)
                {
                    toastCanvasGroup.alpha -= Time.deltaTime / fadeDuration;

                    if (toastCanvasGroup.alpha <= 0f)
                    {
                        toastCanvasGroup.alpha = 0f;
                        _isShowing = false;
                    }
                }
                else
                {
                    _isShowing = false;
                }
            }
        }

        private string GetMessageForType(NotificationType type)
        {
            return type switch
            {
                NotificationType.InsufficientFunds => "Not enough resources!",
                NotificationType.InvalidPlacement => "Invalid placement!",
                NotificationType.ProductionQueueFull => "Production queue full!",
                _ => "Unknown error"
            };
        }
    }
}