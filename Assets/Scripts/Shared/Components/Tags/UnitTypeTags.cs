using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 역할 태그
    public struct HeroTag : IComponentData { }
    public struct WorkerTag : IComponentData { }
    public struct SoldierTag : IComponentData { }
    public struct BuilderTag : IComponentData { }

    // 병사 세부 유형 태그
    public struct StrikerTag : IComponentData { }  // 근접 병사
    public struct ArcherTag : IComponentData { }    // 중거리 병사
    public struct TankTag : IComponentData { }     // 장거리 병사

    // 필요하다면 속성 태그도 가능
    // [GhostComponent] public struct BiologicalTag : IComponentData { } // 생체 유닛 (메딕 치료 대상)
    // [GhostComponent] public struct MechanicalTag : IComponentData { } // 기계 유닛 (수리 대상)
}