using System;
using System.Net.Sockets;
using Sos.Room;
using Shared;

namespace Server
{
    public struct TokenValidateResult
    {
        public bool Valid;
        public string UserId;
        public string SessionId;
    }

    /// <summary>
    /// 룸 서버 내부 채널(:8081)에 동기 TCP 연결하여 토큰 검증을 수행한다.
    /// TokenValidationSystem이 소유하며, 요청-응답 패턴으로 동작한다.
    /// </summary>
    public class RoomTokenValidator : IDisposable
    {
        TcpClient tcpClient;
        NetworkStream stream;
        readonly string host;
        readonly ushort port;

        byte[] receiveBuffer = new byte[1024];

        public RoomTokenValidator(string host, ushort port)
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

            // 기존 연결 정리
            CloseConnection();

            tcpClient = new TcpClient();
            tcpClient.Connect(host, port);
            stream = tcpClient.GetStream();
        }

        /// <summary>
        /// 토큰을 룸 서버에 전송하고 검증 결과를 동기적으로 반환한다.
        /// localhost 통신이므로 지연은 1ms 미만.
        /// </summary>
        public TokenValidateResult Validate(string token)
        {
            try
            {
                EnsureConnected();

                // 1. TokenValidateRequest를 Envelope로 감싸서 전송
                var envelope = new Envelope
                {
                    TokenValidateRequest = new TokenValidateRequest
                    {
                        AuthToken = token
                    }
                };

                byte[] framedData = ProtobufFraming.Frame(envelope);
                stream.Write(framedData, 0, framedData.Length);

                // 2. 동기 수신 루프 - TryDeframe 성공할 때까지 읽기
                int totalReceived = 0;
                int offset = 0;

                while (true)
                {
                    int bytesRead = stream.Read(
                        receiveBuffer,
                        totalReceived,
                        receiveBuffer.Length - totalReceived);

                    if (bytesRead == 0)
                    {
                        // 서버가 연결을 닫음
                        CloseConnection();
                        return new TokenValidateResult { Valid = false };
                    }

                    totalReceived += bytesRead;

                    if (ProtobufFraming.TryDeframe(receiveBuffer, ref offset, totalReceived, out var responseEnvelope))
                    {
                        if (responseEnvelope.PayloadCase == Envelope.PayloadOneofCase.TokenValidateResponse)
                        {
                            var response = responseEnvelope.TokenValidateResponse;
                            return new TokenValidateResult
                            {
                                Valid = response.Valid,
                                UserId = response.PlayerId,
                                SessionId = response.SessionId
                            };
                        }

                        // 예상치 못한 응답 타입
                        return new TokenValidateResult { Valid = false };
                    }
                }
            }
            catch (Exception)
            {
                // 연결 오류 시 정리 후 실패 반환
                CloseConnection();
                return new TokenValidateResult { Valid = false };
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
