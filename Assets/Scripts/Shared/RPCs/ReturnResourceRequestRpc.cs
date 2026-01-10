using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 자원 반납 요청 RPC
    /// - 자원을 들고 있는 Worker가 ResourceCenter를 우클릭했을 때 전송
    /// - 서버에서 반납 처리 후 마지막 채굴 장소로 자동 복귀
    /// </summary>
    public struct ReturnResourceRequestRpc : IRpcCommand
    {
        // 반납할 Worker의 GhostId
        public int WorkerGhostId;

        // 반납 대상 ResourceCenter의 GhostId
        public int ResourceCenterGhostId;
    }
}
