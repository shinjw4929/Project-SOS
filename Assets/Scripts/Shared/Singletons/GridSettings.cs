using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct GridSettings : IComponentData
    {
        public float CellSize;
        public float2 GridOrigin;
        public int2 GridSize;

        // 건설 스냅 단위 (셀 수). CellSize × BuildSnapCells = 스냅 거리
        public int BuildSnapCells;
    }
}
