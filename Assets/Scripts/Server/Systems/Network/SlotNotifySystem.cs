using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// 유저 연결 해제 시 룸 서버에 SlotReleased를 전송하고,
    /// 주기적으로 GameServerHeartbeat를 보내 게임 서버 상태를 보고한다.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    partial class SlotNotifySystem : SystemBase
    {
        SlotNotifyClient notifyClient;
        double lastHeartbeatTime;
        const double HeartbeatIntervalSeconds = 30.0;

        protected override void OnCreate()
        {
            notifyClient = new SlotNotifyClient("127.0.0.1", 8081);
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 1. 연결 해제 감지: ConnectionState.Disconnected 상태의 RoomSessionInfo 보유 엔티티
            //    ConnectionState는 ICleanupComponentData이므로 엔티티 파괴 후에도 잔존한다.
            foreach (var (sessionInfo, connectionState, entity) in
                SystemAPI.Query<RefRO<RoomSessionInfo>, RefRO<ConnectionState>>()
                    .WithEntityAccess())
            {
                if (connectionState.ValueRO.CurrentState == ConnectionState.State.Disconnected)
                {
                    var info = sessionInfo.ValueRO;
                    notifyClient.SendSlotReleased(
                        info.UserId.ToString(),
                        info.SessionId.ToString());
                    ecb.RemoveComponent<RoomSessionInfo>(entity);

                    FixedString128Bytes logMessage = "SlotReleased sent, userId=";
                    logMessage.Append(info.UserId);
                    GameLogger.Info(LogWorld.Server, LogCategory.Network, in logMessage);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            // 2. 주기적 하트비트 (30초 간격)
            double currentTime = SystemAPI.Time.ElapsedTime;
            if (currentTime - lastHeartbeatTime >= HeartbeatIntervalSeconds)
            {
                lastHeartbeatTime = currentTime;

                using var sessionQuery = EntityManager.CreateEntityQuery(typeof(RoomSessionInfo));
                int activeSessionCount = sessionQuery.CalculateEntityCount();

                notifyClient.SendHeartbeat("unity-game-server", (uint)activeSessionCount);
            }
        }

        protected override void OnDestroy()
        {
            notifyClient?.Dispose();
        }
    }
}
