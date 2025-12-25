using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    public struct BuildRequestRpc : IRpcCommand
    {
        public BuildingTypeEnum buildingType;
        public int gridX;
        public int gridY;
        public float3 worldPosition;
    }
}
