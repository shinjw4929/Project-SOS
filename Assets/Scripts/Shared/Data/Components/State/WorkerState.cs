using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public enum ResourceType : byte
    {
        None = 0,
        Mineral = 1,
        Gas = 2
    }

    [GhostComponent]
    public struct WorkerState : IComponentData
    {
        // 현재 들고 있는 자원 양
        [GhostField] public int CarriedAmount;
        
        // 들고 있는 자원 종류
        [GhostField] public ResourceType CarriedType;
        
        // 채집 진행도 (자원에 붙어서 캐고 있을 때 0.0 ~ 1.0)
        [GhostField(Quantization = 100)] public float GatheringProgress;
    }
}