using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// [변하는 데이터] 공격관련 스테이터스
    /// </summary>
    [GhostComponent]
    public struct CombatStats : IComponentData
    {
        [GhostField] public float AttackPower;
        [GhostField] public float AttackSpeed;
        [GhostField] public float AttackRange;
    }
}