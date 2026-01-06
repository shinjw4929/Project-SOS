using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct Team : IComponentData
    {
        [GhostField] public int teamId;
    }
}
