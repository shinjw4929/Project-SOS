using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    public enum StructureContext : byte
    {
        Idle = 0,           // 대기 (평상시)
        Constructing = 1,   // 건설 중
        Active = 2,         // 작업 중 (배럭: 빛남, 포탑: 공격중)
        Destroyed = 255     // 파괴됨 (잔해)
    }

    [GhostComponent]
    public struct StructureState : IComponentData
    {
        [GhostField] public StructureContext CurrentState;
    }
}