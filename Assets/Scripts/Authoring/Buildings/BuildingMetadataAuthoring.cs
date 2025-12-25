using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 건물 메타데이터를 엔티티에 베이킹하는 Authoring 컴포넌트
    /// 건물 프리팹에 추가하여 크기, 비용 등을 설정
    /// </summary>
    public class BuildingMetadataAuthoring : MonoBehaviour
    {
        [Header("Grid Size")]
        [Tooltip("그리드 너비 (칸 수)")]
        public int width = 1;

        [Tooltip("그리드 높이 (칸 수)")]
        public int height = 1;

        [Header("Build Cost (Future)")]
        [Tooltip("건설 비용")]
        public int cost = 100;

        [Tooltip("건설 시간 (초)")]
        public float buildTime = 1.0f;

        class Baker : Baker<BuildingMetadataAuthoring>
        {
            public override void Bake(BuildingMetadataAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new BuildingMetadata
                {
                    width = authoring.width,
                    height = authoring.height,
                    cost = authoring.cost,
                    buildTime = authoring.buildTime
                });
            }
        }
    }
}
