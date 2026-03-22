using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 그리드 셀 점유 상태 (DynamicBuffer로 1D 배열 관리)
    /// index = z * gridSizeX + x
    /// byte 사용: Reinterpret + MemSet 초기화 호환, sizeof(GridCell) = 2 byte 고정
    /// </summary>
    public struct GridCell : IBufferElementData
    {
        public byte IsOccupied;      // 건물 배치 점유 (0=비점유, 1=점유)
        public byte IsPathBlocked;   // 경로탐색 차단 (0=통과 가능, 1=차단)
    }
}
