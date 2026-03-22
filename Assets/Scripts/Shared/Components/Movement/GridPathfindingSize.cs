using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 유닛의 경로탐색 크기 (passability 맵 선택에 사용)
    /// CellPadding=0: Small (자기 셀만), CellPadding=1: Large (주변 1칸 포함)
    /// </summary>
    public struct GridPathfindingSize : IComponentData
    {
        public byte CellPadding;
    }
}
