using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 자원 채집 요청 RPC
    /// - 클라이언트가 Worker로 ResourceNode를 우클릭했을 때 전송
    /// - 서버에서 점유 가능 여부 확인 후 채집 시작
    /// </summary>
    public struct GatherRequestRpc : IRpcCommand
    {
        // 채집을 수행할 Worker의 GhostId
        public int WorkerGhostId;

        // 채집 대상 ResourceNode의 GhostId
        public int ResourceNodeGhostId;

        // 반납 지점 ResourceCenter의 GhostId (0이면 자동으로 가장 가까운 곳)
        public int ReturnPointGhostId;
    }
}
