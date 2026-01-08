using Unity.Entities;

namespace Shared
{
    public struct WallTag : IComponentData { }                  // 벽
    public struct ProductionFacilityTag : IComponentData { }    // 유닛 생산
    public struct TurretTag : IComponentData { }                // 공격 타워
    public struct ResourceCenterTag : IComponentData { }        // 자원 반납 지점
}