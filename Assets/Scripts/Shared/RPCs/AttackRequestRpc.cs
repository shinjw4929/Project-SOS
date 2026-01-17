using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 공격 명령 RPC
    /// - 클라이언트가 유닛으로 적을 우클릭했을 때 전송
    /// - 서버에서 소유권/타겟 검증 후 공격 시작
    /// </summary>
    public struct AttackRequestRpc : IRpcCommand
    {
        // 공격할 유닛의 GhostId
        public int UnitGhostId;

        // 공격 대상의 GhostId
        public int TargetGhostId;

        // 대상 위치 (추격용)
        public float3 TargetPosition;
    }
}
