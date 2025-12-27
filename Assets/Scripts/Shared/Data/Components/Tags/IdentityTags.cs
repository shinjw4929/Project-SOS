using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public struct UnitTag : IComponentData { }      // "나는 움직이는 유닛이다"
    public struct StructureTag : IComponentData { } // "나는 고정된 건물이다"
    public struct ProjectileTag : IComponentData { } // "나는 투사체다"
}