using Unity.Entities;

namespace Client
{
    /// <summary>
    /// VAT 클립 전환 감지용 (이전 프레임의 CurrentClipIndex 저장)
    /// - VATAnimationInitSystem에서 VATAnimParam과 함께 부착
    /// </summary>
    public struct PreviousClipIndex : IComponentData
    {
        public byte Value;
    }
}
