using Unity.NetCode;
using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct BuildRequestRpc : IRpcCommand
    {
        public int StructureIndex;
        public int2 GridPosition;
        public int BuilderGhostId;  // 건설자의 GhostId (충돌 검사 제외용)
    }
}
