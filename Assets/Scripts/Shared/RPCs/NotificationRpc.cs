using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 알림 타입 정의
    /// </summary>
    public enum NotificationType : byte
    {
        None = 0,
        InsufficientFunds = 1,      // 자원 부족
        InvalidPlacement = 2,        // 건설 위치 불가
        ProductionQueueFull = 3,     // 생산 대기열 가득 참
        PopulationLimitReached = 4,  // 인구수 초과
    }

    /// <summary>
    /// 서버 → 클라이언트 알림 RPC
    /// </summary>
    public struct NotificationRpc : IRpcCommand
    {
        public NotificationType Type;
    }
}