using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public struct UnitTag : IComponentData { } // 유닛
    public struct StructureTag : IComponentData { } // 건물
    public struct ProjectileTag : IComponentData { } // 투사체
    public struct EnemyTag : IComponentData { } // 적 유닛
    public struct InteractionObstacle : IComponentData { } // 상호작용 장애물
    
}