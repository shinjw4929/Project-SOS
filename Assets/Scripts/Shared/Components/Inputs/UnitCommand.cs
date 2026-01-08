using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 유닛 명령 (ICommandData)
    /// - 클라이언트에서 서버로 전송되는 입력 명령
    /// </summary>
    public struct UnitCommand : ICommandData
    {
        public NetworkTick Tick { get; set; }
        // 명령 타입
        public UnitCommandType CommandType;
        // 유닛이 이동할 위치
        public float3 GoalPosition;
        // 목표 엔티티의 GhostId
        // 명령의 대상이 되는 엔티티가 존재할 시 입력됨
        // Entity의 Id는 서버/클라이언트에서 다르므로 GhostId로 전송
        public int TargetGhostId;
    }

    /// <summary>
    /// RTS 명령 타입
    /// </summary>
    public enum UnitCommandType : byte
    {
        None = 0,            // 명령 없음
        LeftClick = 1,       // 좌클릭
        RightClick = 2,      // 우클릭
        AttackKey = 3,       // 공격키
        StopKey = 4,         // 정지키
        HoldKey = 5,         // 홀드키
        BuildKey = 6,        // 건설키
    }
}
