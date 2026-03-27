using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// VAT 애니메이션 재생 상태 (서버→클라이언트 Ghost 동기화)
    /// - 서버: VATAnimationStateUpdateSystem이 UnitActionState/EnemyState 변화 시 갱신
    /// - 클라이언트: VATAnimationPlaybackSystem이 읽어서 VATAnimParam 계산
    /// </summary>
    [GhostComponent]
    public struct VATAnimationState : IComponentData
    {
        [GhostField] public byte CurrentClipIndex;
        [GhostField(Quantization = 100)] public float AnimStartTime;
    }
}
