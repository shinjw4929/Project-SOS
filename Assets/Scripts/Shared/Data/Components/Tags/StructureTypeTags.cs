using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct WallTag : IComponentData { }     // "벽"
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct ResourceCenterTag : IComponentData { } // "자원 반납처"
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct BarracksTag : IComponentData { } // "공격 유닛 생산"
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct TurretTag : IComponentData { }   // "공격 타워"
    
}