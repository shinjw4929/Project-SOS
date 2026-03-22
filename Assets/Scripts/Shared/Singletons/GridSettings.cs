using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct GridSettings : IComponentData
    {
        public float CellSize;
        public float2 GridOrigin;
        public int2 GridSize;

        // 건설 스냅 단위 (셀 수). CellSize=0.5, BuildSnapCells=2 → 1m 단위 스냅
        public int BuildSnapCells;
    }
}
