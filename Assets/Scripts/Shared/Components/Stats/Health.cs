using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// [변하는 데이터] 현재 체력
    /// </summary>
    [GhostComponent]
    public struct Health : IComponentData
    {
        [GhostField] public float CurrentValue;

        // 최대 체력도 버프/업그레이드로 변할 수 있다면 동기화 필요
        // 만약 절대 안 변한다면 Metadata에서 가져오는 게 이득
        [GhostField] public float MaxValue;
    }
}