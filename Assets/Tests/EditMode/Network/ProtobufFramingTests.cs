using System;
using NUnit.Framework;
using Shared;
using Sos.Room;

namespace Tests.EditMode.Network
{
    [TestFixture]
    public class ProtobufFramingTests
    {
        #region Frame + TryDeframe Roundtrip

        [Test]
        public void FrameAndDeframe_CreateRoomRequest_RoundtripSucceeds()
        {
            var envelope = new Envelope
            {
                CreateRoom = new CreateRoomRequest
                {
                    PlayerId = "user-1",
                    PlayerName = "TestUser",
                    RoomName = "MyRoom",
                    MaxPlayers = 4
                }
            };

            byte[] framed = ProtobufFraming.Frame(envelope);

            int offset = 0;
            bool result = ProtobufFraming.TryDeframe(framed, ref offset, framed.Length, out var parsed);

            Assert.IsTrue(result);
            Assert.AreEqual(framed.Length, offset);
            Assert.AreEqual(Envelope.PayloadOneofCase.CreateRoom, parsed.PayloadCase);
            Assert.AreEqual("user-1", parsed.CreateRoom.PlayerId);
            Assert.AreEqual("TestUser", parsed.CreateRoom.PlayerName);
            Assert.AreEqual("MyRoom", parsed.CreateRoom.RoomName);
            Assert.AreEqual(4u, parsed.CreateRoom.MaxPlayers);
        }

        [Test]
        public void FrameAndDeframe_GameStart_RoundtripSucceeds()
        {
            var envelope = new Envelope
            {
                GameStart = new GameStart
                {
                    SessionId = "session-abc",
                    AuthToken = "token-xyz",
                    GameServerHost = "127.0.0.1",
                    GameServerPort = 7979
                }
            };

            byte[] framed = ProtobufFraming.Frame(envelope);
            int offset = 0;
            bool result = ProtobufFraming.TryDeframe(framed, ref offset, framed.Length, out var parsed);

            Assert.IsTrue(result);
            Assert.AreEqual(Envelope.PayloadOneofCase.GameStart, parsed.PayloadCase);
            Assert.AreEqual("token-xyz", parsed.GameStart.AuthToken);
            Assert.AreEqual(7979u, parsed.GameStart.GameServerPort);
        }

        #endregion

        #region Length Prefix Verification

        [Test]
        public void Frame_LengthPrefix_IsLittleEndian4Bytes()
        {
            var envelope = new Envelope { LeaveRoom = new LeaveRoomRequest() };
            byte[] framed = ProtobufFraming.Frame(envelope);

            int prefixLength = framed[0]
                             | (framed[1] << 8)
                             | (framed[2] << 16)
                             | (framed[3] << 24);

            Assert.AreEqual(framed.Length - ProtobufFraming.HeaderSize, prefixLength);
        }

        #endregion

        #region Partial Receive

        [Test]
        public void TryDeframe_InsufficientHeader_ReturnsFalse()
        {
            byte[] buffer = { 0x05, 0x00, 0x00 }; // 3 bytes, header requires 4
            int offset = 0;
            bool result = ProtobufFraming.TryDeframe(buffer, ref offset, buffer.Length, out var envelope);

            Assert.IsFalse(result);
            Assert.AreEqual(0, offset);
            Assert.IsNull(envelope);
        }

        [Test]
        public void TryDeframe_IncompleteMessage_ReturnsFalse()
        {
            var envelope = new Envelope { Heartbeat = new RoomHeartbeat() };
            byte[] framed = ProtobufFraming.Frame(envelope);

            // 마지막 1바이트를 잘라서 불완전한 데이터 시뮬레이션
            int offset = 0;
            bool result = ProtobufFraming.TryDeframe(framed, ref offset, framed.Length - 1, out _);

            Assert.IsFalse(result);
            Assert.AreEqual(0, offset);
        }

        #endregion

        #region Multiple Messages

        [Test]
        public void TryDeframe_MultipleMessages_ParsesSequentially()
        {
            var env1 = new Envelope { Heartbeat = new RoomHeartbeat() };
            var env2 = new Envelope
            {
                Reject = new RejectResponse
                {
                    Reason = RejectResponse.Types.RejectReason.RoomFull,
                    Message = "Room is full"
                }
            };

            byte[] framed1 = ProtobufFraming.Frame(env1);
            byte[] framed2 = ProtobufFraming.Frame(env2);

            // 두 메시지를 하나의 버퍼에 연결
            byte[] combined = new byte[framed1.Length + framed2.Length];
            Buffer.BlockCopy(framed1, 0, combined, 0, framed1.Length);
            Buffer.BlockCopy(framed2, 0, combined, framed1.Length, framed2.Length);

            int offset = 0;

            // 첫 번째 메시지
            Assert.IsTrue(ProtobufFraming.TryDeframe(combined, ref offset, combined.Length, out var parsed1));
            Assert.AreEqual(Envelope.PayloadOneofCase.Heartbeat, parsed1.PayloadCase);

            // 두 번째 메시지
            Assert.IsTrue(ProtobufFraming.TryDeframe(combined, ref offset, combined.Length, out var parsed2));
            Assert.AreEqual(Envelope.PayloadOneofCase.Reject, parsed2.PayloadCase);
            Assert.AreEqual("Room is full", parsed2.Reject.Message);

            // 더 이상 메시지 없음
            Assert.IsFalse(ProtobufFraming.TryDeframe(combined, ref offset, combined.Length, out _));
        }

        #endregion

        #region Invalid Message Size

        [Test]
        public void TryDeframe_ExceedsMaxSize_ThrowsException()
        {
            // MaxMessageSize + 1을 length prefix로 인코딩
            int invalidSize = ProtobufFraming.MaxMessageSize + 1;
            byte[] buffer = new byte[ProtobufFraming.HeaderSize + 4];
            buffer[0] = (byte)(invalidSize);
            buffer[1] = (byte)(invalidSize >> 8);
            buffer[2] = (byte)(invalidSize >> 16);
            buffer[3] = (byte)(invalidSize >> 24);

            int offset = 0;
            Assert.Throws<InvalidOperationException>(() =>
                ProtobufFraming.TryDeframe(buffer, ref offset, buffer.Length, out _));
        }

        #endregion
    }
}
