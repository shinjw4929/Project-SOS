using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// RTS 유닛 명령 (ICommandData)
    /// - 클라이언트에서 서버로 전송되는 입력 명령
    /// - 예측 시뮬레이션에서 사용
    /// </summary>
    public struct RTSCommand : ICommandData
    {
        public NetworkTick Tick { get; set; }

        // 명령 타입
        public RTSCommandType CommandType;

        // 목표 위치 (Move, AttackMove, Patrol 등)
        public float3 TargetPosition;

        // 목표 엔티티의 GhostId (Attack, AttackMove 시 대상)
        // Entity는 서버/클라이언트에서 다르므로 GhostId로 전송
        public int TargetGhostId;
    }

    /// <summary>
    /// RTS 명령 타입
    /// </summary>
    public enum RTSCommandType : byte
    {
        None = 0,           // 명령 없음
        Move = 1,           // 이동 (우클릭 지면)
        Attack = 2,         // 공격 (A + 클릭)
        AttackMove = 3,     // 이동 + 공격 (우클릭 적 또는 A + 클릭 지면)
        Stop = 4,           // 정지 (S)
        Patrol = 5,         // 순찰 (P)
        Hold = 6,           // 대기 (H)
    }
}
