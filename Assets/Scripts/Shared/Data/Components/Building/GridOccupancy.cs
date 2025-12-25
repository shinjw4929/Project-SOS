using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct GridOccupancy : IComponentData
    {
        [GhostField] public int gridX;
        [GhostField] public int gridY;
        [GhostField] public int width;
        [GhostField] public int height;
    }
}
