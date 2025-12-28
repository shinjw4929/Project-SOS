using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 클라이언트 → 서버: 벽 자폭 요청
    /// </summary>
    public struct SelfDestructRequestRpc : IRpcCommand
    {
        /// <summary>자폭할 건물의 Ghost ID</summary>
        public int TargetGhostId;
    }
}
