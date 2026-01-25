using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 서버 → 클라이언트: 게임오버 브로드캐스트
    /// 모든 유저의 Hero가 사망했을 때 전송
    /// </summary>
    public struct GameOverRpc : IRpcCommand { }
}
