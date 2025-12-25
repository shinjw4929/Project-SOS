using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct UnitType : IComponentData
    {
        [GhostField] public UnitTypeEnum type;
    }

    public enum UnitTypeEnum : byte
    {
        Infantry = 0,
        Tank = 1,
        Scout = 2,
        Artillery = 3,
    }
}
