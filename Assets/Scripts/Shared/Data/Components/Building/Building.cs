using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct Building : IComponentData
    {
        [GhostField] public BuildingTypeEnum buildingType;
        [GhostField] public int ownerTeamId;
    }
}
