using System;
using System.Net.Sockets;
using Sos.Room;
using Shared;
using Unity.Collections;

namespace Server
{
    /// <summary>
    /// 룸 서버에 슬롯 해제(SlotReleased) 및 하트비트(GameServerHeartbeat)를 전송하는 TCP 클라이언트.
    /// SlotNotifySystem이 소유하며, fire-and-forget 패턴으로 동작한다.
    /// 전송 실패 시 경고 로그만 남기고 계속 진행한다 (룸 서버 TTL이 복구를 처리).
    /// </summary>
    public class SlotNotifyClient : IDisposable
    {
        TcpClient tcpClient;
        NetworkStream stream;
        readonly string host;
        readonly ushort port;

        public SlotNotifyClient(string host, ushort port)
        {
            this.host = host;
            this.port = port;
        }

        /// <summary>
        /// TCP 연결이 없거나 끊어졌으면 새로 연결한다.
        /// </summary>
        void EnsureConnected()
        {
            if (tcpClient != null && tcpClient.Connected)
                return;

            CloseConnection();

            tcpClient = new TcpClient();
            tcpClient.Connect(host, port);
            stream = tcpClient.GetStream();
        }

        /// <summary>
        /// 유저 연결 해제 시 룸 서버에 슬롯 해제를 알린다. fire-and-forget.
        /// </summary>
        public void SendSlotReleased(string userId, string sessionId)
        {
            var envelope = new Envelope
            {
                SlotReleased = new SlotReleased
                {
                    PlayerId = userId,
                    SessionId = sessionId
                }
            };
            SendEnvelopeSafe(envelope, "SlotReleased");
        }

        /// <summary>
        /// 주기적으로 게임 서버 상태를 룸 서버에 보고한다. fire-and-forget.
        /// </summary>
        public void SendHeartbeat(string serverId, uint activeSessions)
        {
            var envelope = new Envelope
            {
                GameServerHeartbeat = new GameServerHeartbeat
                {
                    ServerId = serverId,
                    ActiveSessions = activeSessions
                }
            };
            SendEnvelopeSafe(envelope, "GameServerHeartbeat");
        }

        /// <summary>
        /// Envelope를 전송한다. 실패 시 경고 로그만 남기고 연결을 정리한다.
        /// </summary>
        void SendEnvelopeSafe(Envelope envelope, string messageType)
        {
            try
            {
                EnsureConnected();
                byte[] framedData = ProtobufFraming.Frame(envelope);
                stream.Write(framedData, 0, framedData.Length);
            }
            catch (Exception)
            {
                CloseConnection();

                FixedString128Bytes logMessage = "SlotNotify send failed: ";
                logMessage.Append(messageType);
                GameLogger.Warning(LogWorld.Server, LogCategory.Network, in logMessage);
            }
        }

        void CloseConnection()
        {
            if (stream != null)
            {
                try { stream.Close(); } catch { /* 무시 */ }
                stream = null;
            }

            if (tcpClient != null)
            {
                try { tcpClient.Close(); } catch { /* 무시 */ }
                tcpClient = null;
            }
        }

        public void Dispose()
        {
            CloseConnection();
        }
    }
}
