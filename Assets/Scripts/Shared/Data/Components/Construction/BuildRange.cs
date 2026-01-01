using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// Builder 유닛의 건설 사거리
    /// 유닛 중심에서 건물 AABB 최근접점까지의 거리로 계산
    /// </summary>
    [GhostComponent]
    public struct BuildRange : IComponentData
    {
        [GhostField] public float Value;
    }
}
