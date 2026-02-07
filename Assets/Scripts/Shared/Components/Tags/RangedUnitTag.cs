using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 원거리 유닛 태그
    /// - RangedAttackSystem에서 처리
    /// - Archer, Tank 등에 추가
    /// </summary>
    public struct RangedUnitTag : IComponentData { }
}
