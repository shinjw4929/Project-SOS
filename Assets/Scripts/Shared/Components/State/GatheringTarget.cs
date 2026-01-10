using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// Worker의 현재 채집 대상 및 반납 지점 정보
    /// </summary>
    [GhostComponent]
    public struct GatheringTarget : IComponentData
    {
        // 목표 자원 노드 (채집 대상)
        [GhostField] public Entity ResourceNodeEntity;

        // 자원 반납 지점 (ResourceCenter)
        [GhostField] public Entity ReturnPointEntity;

        // 자동 복귀 여부 (true: 반납 후 자동으로 ResourceNode로 돌아감)
        [GhostField] public bool AutoReturn;

        // 마지막으로 채굴한 노드 (반납 후 복귀용)
        [GhostField] public Entity LastGatheredNodeEntity;
    }
}
