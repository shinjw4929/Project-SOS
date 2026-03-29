using Unity.Collections;
using Unity.Entities;

namespace Client
{
    /// <summary>
    /// 룸 서버 GameStart에서 수신한 인증 토큰을 ECS에 전달하는 싱글톤.
    /// RoomClient(MonoBehaviour)가 토큰을 기록하고, GoInGameClientSystem이 읽어 RPC에 포함한다.
    /// </summary>
    public struct RoomAuthState : IComponentData
    {
        /// <summary>룸 서버 발급 인증 토큰 (Length == 0이면 미발급 상태)</summary>
        public FixedString128Bytes AuthToken;

        /// <summary>세션 식별자</summary>
        public FixedString128Bytes SessionId;
    }
}
