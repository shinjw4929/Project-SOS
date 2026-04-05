using System;
using Google.Protobuf;

namespace Shared
{
    /// <summary>
    /// Protobuf 메시지의 4byte LE length-prefix 프레이밍 유틸리티.
    /// TCP 스트림에서 메시지 경계를 구분하기 위한 직렬화/역직렬화를 제공한다.
    /// Room(Envelope), Chat(ChatEnvelope) 등 모든 Protobuf 메시지 타입에 범용 사용.
    /// </summary>
    public static class ProtobufFraming
    {
        /// <summary>프레이밍 헤더 크기 (4byte little-endian length prefix)</summary>
        public const int HeaderSize = 4;

        /// <summary>최대 메시지 크기 (1MB, 백엔드와 동일)</summary>
        public const int MaxMessageSize = 1024 * 1024;

        /// <summary>
        /// Protobuf 메시지를 직렬화하고 4byte LE length prefix를 붙인 바이트 배열을 반환한다.
        /// </summary>
        public static byte[] Frame<T>(T message) where T : IMessage<T>
        {
            byte[] messageBytes = message.ToByteArray();
            int messageSize = messageBytes.Length;
            byte[] result = new byte[HeaderSize + messageSize];

            // 4byte little-endian length prefix
            result[0] = (byte)(messageSize);
            result[1] = (byte)(messageSize >> 8);
            result[2] = (byte)(messageSize >> 16);
            result[3] = (byte)(messageSize >> 24);

            // 직렬화된 바이트 복사
            Buffer.BlockCopy(messageBytes, 0, result, HeaderSize, messageSize);

            return result;
        }

        /// <summary>
        /// 수신 버퍼에서 완전한 Protobuf 메시지를 추출한다.
        /// </summary>
        /// <param name="buffer">수신 버퍼</param>
        /// <param name="offset">읽기 시작 위치. 성공 시 다음 메시지 시작 위치로 전진</param>
        /// <param name="available">버퍼 내 유효 바이트 수 (0부터 available-1까지 유효)</param>
        /// <param name="parser">Protobuf 메시지 파서 (예: Envelope.Parser, ChatEnvelope.Parser)</param>
        /// <param name="message">파싱된 메시지 (성공 시)</param>
        /// <returns>완전한 메시지를 추출했으면 true</returns>
        public static bool TryDeframe<T>(byte[] buffer, ref int offset, int available,
            MessageParser<T> parser, out T message) where T : IMessage<T>
        {
            message = default;

            int remaining = available - offset;

            // 헤더(4byte)를 읽을 수 있는지 확인
            if (remaining < HeaderSize)
                return false;

            // 4byte little-endian length 읽기
            int messageSize = buffer[offset]
                            | (buffer[offset + 1] << 8)
                            | (buffer[offset + 2] << 16)
                            | (buffer[offset + 3] << 24);

            // 메시지 크기 검증
            if (messageSize < 0 || messageSize > MaxMessageSize)
                throw new InvalidOperationException(
                    $"Invalid message size: {messageSize} (max: {MaxMessageSize})");

            // 완전한 메시지가 버퍼에 있는지 확인
            if (remaining < HeaderSize + messageSize)
                return false;

            // Protobuf 역직렬화
            message = parser.ParseFrom(buffer, offset + HeaderSize, messageSize);
            offset += HeaderSize + messageSize;

            return true;
        }
    }
}
