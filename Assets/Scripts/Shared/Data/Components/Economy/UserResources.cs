using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct UserResources : IComponentData
    {
        // 1. 자원
        [GhostField] public int Resources;
        
        // 2. 인구수 (Current / Max)
        // 유닛 생산 시: Current + Cost <= Max 인지 확인
        [GhostField] public int CurrentPopulation; // 현재 사용 중인 인구
        [GhostField] public int MaxPopulation;     // 최대 인구
    }
}