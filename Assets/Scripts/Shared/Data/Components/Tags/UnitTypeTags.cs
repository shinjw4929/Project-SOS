using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public struct HeroTag : IComponentData { }   // "나는 영웅(Avatar)"
    public struct WorkerTag : IComponentData { } // "나는 일꾼"
    public struct SoldierTag : IComponentData { } // "나는 군인"
    public struct BuilderTag : IComponentData { } // "나는 건설 가능한 유닛이다"
    // 필요하다면 속성 태그도 가능
    // [GhostComponent] public struct BiologicalTag : IComponentData { } // 생체 유닛 (메딕 치료 대상)
    // [GhostComponent] public struct MechanicalTag : IComponentData { } // 기계 유닛 (수리 대상)
}