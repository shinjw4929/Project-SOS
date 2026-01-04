using Shared;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Authoring
{
    public class ResourceNodeAuthoring : MonoBehaviour
    {
        [Header("Grid Size")]
        [Min(1)] public int width = 1;
        [Min(1)] public int length = 1;
        [Min(0.1f)] public float height = 1f;
        
        public class Baker : Baker<ResourceNodeAuthoring>
        {
            public override void Bake(ResourceNodeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // 태그
                AddComponent(entity, new ResourceNodeTag());
                

                // 크기 (풋프린트)
                AddComponent(entity, new StructureFootprint
                {
                    Width = authoring.width,
                    Length = authoring.length,
                    Height = authoring.height
                });

                // 그리드 위치
                AddComponent(entity, new GridPosition { Position = int2.zero });

                // NavMesh Obstacle 경로 탐색 장애물 (서버 전용)
                // ObstacleGridInitSystem에서 활성화됨
                AddComponent(entity, new NeedsNavMeshObstacle());
                SetComponentEnabled<NeedsNavMeshObstacle>(entity, false); // 초기 비활성화
            }
        }
    }
}
