using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct BuildRequestRpc : IRpcCommand
    {
        public int StructureIndex;
        public int2 GridPosition;
    }
}
