using UnityEngine;

namespace Shared
{
    /// <summary>
    /// VAT 베이킹 출력물을 담는 ScriptableObject
    /// - Phase 2 베이킹 툴이 자동 생성
    /// - Phase 4 Authoring Baker에서 BlobAssetReference로 변환
    /// </summary>
    [CreateAssetMenu(fileName = "VATClipData", menuName = "VAT/Clip Data")]
    public class VATClipDataAsset : ScriptableObject
    {
        public Texture2D PositionTexture; // RGBAHalf Position Texture
        public Mesh StaticMesh;           // 바인드포즈 정적 메시 (UV2 버텍스 인덱스 인코딩 포함)

        [System.Serializable]
        public struct ClipEntry
        {
            public string Name;
            public int StartRow;
            public int RowCount;
            public float Fps;
            public bool Loop;
        }

        public ClipEntry[] Clips;
    }
}
