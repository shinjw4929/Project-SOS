using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // [GhostComponent]가 있어야 클라이언트에서 태그를 확인하고 UI/애니메이션 처리가 가능
    [GhostComponent] public struct UnitTag : IComponentData { }      // "나는 움직이는 유닛이다"
    [GhostComponent] public struct StructureTag : IComponentData { } // "나는 고정된 건물이다"
    [GhostComponent] public struct ProjectileTag : IComponentData { } // "나는 투사체다"
}