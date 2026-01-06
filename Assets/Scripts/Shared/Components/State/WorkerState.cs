using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public enum ResourceType : byte
    {
        None = 0,
        Cheese = 1,
    }

    [GhostComponent]
    public struct WorkerState : IComponentData
    {
        // 1. [유지] 현재 들고 있는 자원 양
        // 수시로 변하므로 State에 있어야 함
        [GhostField] public int CarriedAmount;

        [GhostField] public ResourceType CarriedType;

        // 채집 진행도 (0.0 ~ 1.0)
        [GhostField(Quantization = 100)] public float GatheringProgress;

        [GhostField] public bool IsInsideNode;
    }
}