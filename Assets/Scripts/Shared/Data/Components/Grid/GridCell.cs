using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 그리드 셀 점유 상태 (DynamicBuffer로 1D 배열 관리)
    /// index = y * gridWidth + x
    /// </summary>
    public struct GridCell : IBufferElementData
    {
        public bool isOccupied;
    }
}
