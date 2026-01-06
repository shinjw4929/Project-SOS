using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 인구수
    [GhostComponent]
    public struct UserSupply : IComponentData
    {
        [GhostField] public int Currentvalue;
        [GhostField] public int MaxValue;

        // 편의를 위한 읽기 전용 메서드
        public readonly bool CanProduce(int cost) => Currentvalue + cost <= MaxValue;
    }
}