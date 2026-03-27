using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Client
{
    /// <summary>
    /// VAT 셰이더에 per-entity로 주입되는 애니메이션 파라미터
    /// - Entities Graphics가 매 프레임 GPU에 업로드
    /// - x = normalizedTime (0~1), y = clipStartRow(정규화), z = clipRowCount(정규화), w = reserved
    /// </summary>
    [MaterialProperty("_VATAnimParam")]
    public struct VATAnimParam : IComponentData
    {
        public float4 Value;
    }
}
