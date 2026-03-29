using Shared;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Client
{
    /// <summary>
    /// Netcode for Entities 수동 연결 유틸리티.
    /// 룸 서버로부터 받은 호스트/포트 정보로 클라이언트 월드를 연결한다.
    /// </summary>
    public static class NetcodeConnectionUtil
    {
        /// <summary>
        /// 클라이언트 월드에서 지정한 호스트:포트로 Netcode 연결을 시도한다.
        /// </summary>
        /// <param name="clientWorld">연결할 클라이언트 World</param>
        /// <param name="host">서버 IP 주소 (예: "127.0.0.1")</param>
        /// <param name="port">서버 포트</param>
        public static void Connect(World clientWorld, string host, ushort port)
        {
            using var query = clientWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<NetworkStreamDriver>());

            if (query.IsEmpty)
            {
                FixedString128Bytes errMsg = "NetworkStreamDriver not found in world";
                GameLogger.Error(LogWorld.Client, LogCategory.Network, in errMsg);
                return;
            }

            NetworkEndpoint endpoint;
            try
            {
                endpoint = NetworkEndpoint.Parse(host, port);
            }
            catch (System.Exception)
            {
                FixedString128Bytes errMsg = "Failed to parse endpoint: ";
                errMsg.Append(host);
                GameLogger.Error(LogWorld.Client, LogCategory.Network, in errMsg);
                return;
            }

            query.GetSingletonRW<NetworkStreamDriver>().ValueRW
                .Connect(clientWorld.EntityManager, endpoint);

            FixedString128Bytes logMessage = "Connect requested: ";
            logMessage.Append(host);
            logMessage.Append((FixedString32Bytes)":");
            logMessage.Append(port);
            GameLogger.Info(LogWorld.Client, LogCategory.Network, in logMessage);
        }
    }
}
