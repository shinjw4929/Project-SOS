using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 자원 노드의 실시간 상태 (동기화 대상)
    /// </summary>
    [GhostComponent]
    public struct ResourceNodeState : IComponentData
    {
        // 1. 현재 점유 중인 워커
        [GhostField] public Entity OccupyingWorker;
        // 2. 현재 잔여 자원량(무한)
        // UI 표시 및 고갈(Depleted) 체크용
        // [GhostField] public int CurrentAmount;
    }
}