using Unity.Entities;
using Shared;

namespace Client
{
    /// <summary>
    /// 클라이언트 알림 상태 싱글톤
    /// UI에서 이 상태를 읽어 토스트 메시지를 표시
    /// </summary>
    public struct NotificationState : IComponentData
    {
        /// <summary>표시할 알림 타입 (None이면 표시 안 함)</summary>
        public NotificationType PendingNotification;
    }
}