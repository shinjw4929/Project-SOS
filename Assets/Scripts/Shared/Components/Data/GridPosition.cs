using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct GridPosition : IComponentData
    {
        [GhostField] public int2 Position;
    }
}