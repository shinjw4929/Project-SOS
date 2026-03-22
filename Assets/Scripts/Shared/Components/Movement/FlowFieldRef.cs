using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Flow Field 캐시 조회 키 (destinationKey = destCell.y * gridSizeX + destCell.x)
    /// Key=-1: 아직 Flow Field가 할당되지 않은 초기 상태
    /// </summary>
    public struct FlowFieldRef : IComponentData
    {
        public int Key;
    }
}
