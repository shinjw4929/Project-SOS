using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct GridSettings : IComponentData
    {
        public float CellSize;
        public float2 GridOrigin;
        public int2 GridSize;
    }
}
