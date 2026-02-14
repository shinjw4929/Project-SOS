using Unity.Entities;

namespace Shared
{
    // 유닛의 현재 행동 상태 (Enum)
    public enum EnemyContext : byte
    {
        Idle = 0,           // 대기
        Wandering = 1,         // 배회 중
        Attacking = 2,      // 공격 중 (쿨타임 대기 포함)
        Chasing = 3,        // 적 추격 중
        Dormant = 10,       // stuck 후 휴면 (이동/pathfinding 중단)
        Disabled = 20,
        Dying = 254,
        Dead = 255          // 사망
    }

    public struct EnemyState : IComponentData
    {
        // 1. 현재 상태 (Enum)
        public EnemyContext CurrentState;
    }
}