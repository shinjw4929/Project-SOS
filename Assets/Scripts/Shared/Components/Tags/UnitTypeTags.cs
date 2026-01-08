using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public struct HeroTag : IComponentData { }
    public struct WorkerTag : IComponentData { }
    public struct SoldierTag : IComponentData { }
    public struct BuilderTag : IComponentData { }
    // 필요하다면 속성 태그도 가능
    // [GhostComponent] public struct BiologicalTag : IComponentData { } // 생체 유닛 (메딕 치료 대상)
    // [GhostComponent] public struct MechanicalTag : IComponentData { } // 기계 유닛 (수리 대상)
}