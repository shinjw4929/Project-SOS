using Unity.Entities;
using Unity.NetCode;

namespace Shared

{
    /// <summary>
    /// 유닛의 동작을 나타내는 State
    /// - 애니메이션 및 물리 이동 시스템이 참조
    /// </summary>
    [GhostComponent]
    public struct UnitActionState : IComponentData
    {
        [GhostField] public Action State;
    }

    public enum Action : byte
    {
        Idle = 0, // 대기
        Moving = 1, // 이동 중
        Working = 2,
        Attacking = 3, // 공격 중
        Stop = 4, // 정지(공격 불가)
        Holding = 5, // 경계(공격 가능)
        Disabled = 200,
        Dying = 254,
        Dead = 255 // 사망
    }
}