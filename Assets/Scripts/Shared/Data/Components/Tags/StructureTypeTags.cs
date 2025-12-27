using Unity.Entities;

namespace Shared
{
    public struct WallTag : IComponentData { }     // "벽"
    public struct ResourceCenterTag : IComponentData { } // "자원 반납처"
    public struct BarracksTag : IComponentData { } // "공격 유닛 생산"
    public struct TurretTag : IComponentData { }   // "공격 타워"
}