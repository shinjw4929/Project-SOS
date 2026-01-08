using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 자원
    [GhostComponent]
    public struct UserCurrency : IComponentData
    {
        [GhostField] public int Amount;
    }
}