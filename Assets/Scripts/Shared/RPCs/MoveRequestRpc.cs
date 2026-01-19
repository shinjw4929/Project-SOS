using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 이동 명령 RPC
    /// - 클라이언트가 유닛으로 땅을 우클릭했을 때 전송
    /// - 서버에서 소유권 검증 후 이동 시작
    /// - IsAttackMove가 true면 이동 중 적 발견 시 교전 (A+클릭)
    /// </summary>
    public struct MoveRequestRpc : IRpcCommand
    {
        // 이동할 유닛의 GhostId
        public int UnitGhostId;

        // 목표 위치
        public float3 TargetPosition;

        // 공격 이동 여부 (A+클릭)
        // - false: 일반 이동 (적 무시, 후퇴 보장)
        // - true: 공격 이동 (이동 중 적 발견 시 교전)
        public bool IsAttackMove;
    }
}
