using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 유닛의 현재 행동 상태 (Enum)
    public enum UnitContext : byte
    {
        Idle = 0,            // 대기
        Moving = 1,          // 이동 중
        Attacking = 2,       // 공격 중 (쿨타임 대기 포함)
        Chasing = 3,         // 적 추격 중
        Stop = 4,            // 정지
        Holding = 5,         // 대기
        Constructing = 10,   // 건설 중 (일꾼이 건물 짓는 모션)
        MovingToBuild = 11,  // 건설 위치로 이동 중 (사거리 밖 건설 명령)
        Gathering = 20,      // 자원 채집 중 (노드 내부에서 채집)
        MovingToGather = 21, // 자원 노드로 이동 중
        MovingToReturn = 22, // 반납 지점(ResourceCenter)으로 이동 중
        Unloading = 23,
        WaitingForNode = 24, // 자원 노드 대기 상태
        Disabled = 200,
        Dying = 254,
        Dead = 255           // 사망
    }

    [GhostComponent]
    public struct UnitState : IComponentData
    {
        // 1. 현재 상태 (Enum)
        [GhostField] public UnitContext CurrentState;
    }
}