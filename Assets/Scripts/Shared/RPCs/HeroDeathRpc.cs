using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 서버 → 클라이언트: Hero 사망 알림
    /// 해당 유저의 Hero가 사망했음을 알림
    /// </summary>
    public struct HeroDeathRpc : IRpcCommand { }
}
