using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// VAT 애니메이션 Authoring. Hero, EnemySmall, EnemyFlying 프리팹에 추가.
    /// Composition 패턴: 기존 UnitAuthoring/EnemyAuthoring 수정 불필요.
    /// </summary>
    public class VATAnimationAuthoring : MonoBehaviour
    {
        [Tooltip("Phase 2 베이킹 출력물 (Assets/VATData/{모델명}/{모델명}_ClipData.asset)")]
        public VATClipDataAsset ClipData;

        public class Baker : Baker<VATAnimationAuthoring>
        {
            public override void Bake(VATAnimationAuthoring authoring)
            {
                if (authoring.ClipData == null)
                {
                    Debug.LogError(
                        $"[VAT] VATAnimationAuthoring on '{authoring.gameObject.name}' has no ClipData assigned. " +
                        "Animation will not play.", authoring.gameObject);
                    return;
                }

                var entity = GetEntity(TransformUsageFlags.Dynamic);

                // VATAnimationState 초기화 (서버에서 갱신, Ghost 동기화)
                AddComponent(entity, new VATAnimationState
                {
                    CurrentClipIndex = 0,
                    AnimStartTime = 0
                });

                // BlobBuilder: VATClipDataAsset(ScriptableObject) -> VATClipBlobData(BlobAsset) 변환
                var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<VATClipBlobData>();
                root.TextureHeight = authoring.ClipData.PositionTexture.height;

                var clips = builder.Allocate(ref root.Clips, authoring.ClipData.Clips.Length);
                for (int i = 0; i < authoring.ClipData.Clips.Length; i++)
                {
                    var src = authoring.ClipData.Clips[i];
                    clips[i] = new VATClipInfo
                    {
                        StartRow = src.StartRow,
                        RowCount = src.RowCount,
                        Fps = src.Fps,
                        Loop = src.Loop
                    };
                }

                var blobRef = builder.CreateBlobAssetReference<VATClipBlobData>(Allocator.Persistent);
                AddBlobAsset(ref blobRef, out _);
                builder.Dispose();

                AddComponent(entity, new VATClipLibrary { Value = blobRef });
            }
        }
    }
}
