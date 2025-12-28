using Unity.Entities;

namespace Shared
{
    public struct WallTag : IComponentData { }     // 벽
    public struct ProductionFacilityTag :  IComponentData { } // 유닛 생산
    public struct TurretTag : IComponentData { }   // 공격 타워

}