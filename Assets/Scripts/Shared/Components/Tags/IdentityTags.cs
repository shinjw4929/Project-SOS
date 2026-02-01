using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public struct UnitTag : IComponentData { } // 유닛
    public struct StructureTag : IComponentData { } // 건물
    public struct ProjectileTag : IComponentData { } // 투사체
    public struct EnemyTag : IComponentData { } // 적 유닛
    public struct FlyingTag : IComponentData { } // 공중 유닛 (벽 무시, 지상 충돌 분리)
    public struct ResourceNodeTag : IComponentData { } // 자원
    
}