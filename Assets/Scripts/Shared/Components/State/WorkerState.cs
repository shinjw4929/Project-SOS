using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public enum ResourceType : byte
    {
        None = 0,
        Cheese = 1,
    }

    /// <summary>
    /// Worker 채집 사이클의 세부 단계
    /// UnitIntentState.Intent.Gather와 함께 사용하여 정확한 상태 파악
    /// </summary>
    public enum GatherPhase : byte
    {
        None = 0,           // 채집 중이 아님
        MovingToNode = 1,   // 자원 노드로 이동 중
        Gathering = 2,      // 자원 채집 중
        MovingToReturn = 3, // 반납 지점으로 이동 중
        Unloading = 4,      // 자원 하차 중
        WaitingForNode = 5  // 노드 점유 대기 중
    }

    [GhostComponent]
    public struct WorkerState : IComponentData
    {
        // 현재 들고 있는 자원 양
        [GhostField] public int CarriedAmount;

        // 자원 종류
        [GhostField] public ResourceType CarriedType;

        // 채집 진행도 (0.0 ~ 1.0)
        [GhostField(Quantization = 100)] public float GatheringProgress;

        // 자원 노드 내부에 있는지 여부
        [GhostField] public bool IsInsideNode;

        // 채집 사이클의 현재 단계
        [GhostField] public GatherPhase Phase;
    }
}