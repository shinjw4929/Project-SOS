using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 경제/알림 공통 유틸리티 (Burst 호환 static 메서드)
    /// </summary>
    [BurstCompile]
    public static class EconomyUtility
    {
        /// <summary>
        /// 알림 RPC 전송 (connection이 Null이면 무시)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SendNotification(
            ref EntityCommandBuffer ecb,
            Entity targetConnection,
            NotificationType type)
        {
            if (targetConnection == Entity.Null) return;
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new NotificationRpc { Type = type });
            ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = targetConnection });
        }
    }
}
