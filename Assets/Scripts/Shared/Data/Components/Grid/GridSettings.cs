using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct GridSettings : IComponentData
    {
        public float cellSize;
        public float2 gridOrigin;
    }
}
