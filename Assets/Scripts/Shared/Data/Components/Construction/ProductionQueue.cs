using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct ProductionQueue : IComponentData
    {
        // [핵심] Enum 대신, 생산하려는 "원본 프리팹 엔티티"를 저장합니다.
        // Netcode는 GhostPrefab으로 등록된 엔티티의 동기화를 지원합니다.
        [GhostField] public Entity ProducingPrefab; 
        
        [GhostField] public float Progress;
        [GhostField] public float Duration;
        [GhostField] public bool IsActive;
    }
}