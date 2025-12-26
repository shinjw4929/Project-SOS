using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// [변하는 데이터] 현재 적용 중인 실시간 스탯 (버프 적용됨)
    /// </summary>
    [GhostComponent]
    public struct Defense : IComponentData
    {
        [GhostField] public float Value;
    }
}