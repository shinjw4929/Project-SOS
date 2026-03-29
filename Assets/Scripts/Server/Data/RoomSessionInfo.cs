using Unity.Collections;
using Unity.Entities;

namespace Server
{
    /// <summary>
    /// 토큰 검증 성공 시 Connection 엔티티에 부착.
    /// SlotNotifySystem이 연결 끊김 시 이 정보를 읽어 SlotReleased를 전송한다.
    /// </summary>
    public struct RoomSessionInfo : IComponentData
    {
        public FixedString64Bytes SessionId;
        public FixedString64Bytes UserId;  // Proto의 player_id 대응, Unity 측은 "User" 네이밍
    }
}
