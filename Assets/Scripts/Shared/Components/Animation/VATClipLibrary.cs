using Unity.Entities;

namespace Shared
{
    public struct VATClipInfo
    {
        public int StartRow;
        public int RowCount;
        public float Fps;
        public bool Loop; // BlobAsset 내부 읽기전용 -> MarshalAs 불필요
    }

    public struct VATClipBlobData
    {
        public BlobArray<VATClipInfo> Clips; // 인덱스 = CurrentClipIndex
        public int TextureHeight;            // 총 텍스처 높이 (정규화 계산용)
    }

    /// <summary>
    /// VAT 클립 정보 BlobAsset 참조
    /// - Authoring(Phase 4)에서 VATClipDataAsset → BlobBuilder로 생성
    /// - PlaybackSystem이 클립별 StartRow/RowCount/Fps/Loop 조회에 사용
    /// </summary>
    public struct VATClipLibrary : IComponentData
    {
        public BlobAssetReference<VATClipBlobData> Value;
    }
}
