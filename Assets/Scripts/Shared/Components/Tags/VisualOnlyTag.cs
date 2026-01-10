using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 시각 전용 투사체 태그
    /// - 데미지를 주지 않음
    /// - CombatDamageSystem에서 무시됨
    /// </summary>
    public struct VisualOnlyTag : IComponentData { }
}
