using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct MoveTarget : IComponentData
    {
        [GhostField] public float3 position;
        [GhostField] public bool isValid;
    }
}