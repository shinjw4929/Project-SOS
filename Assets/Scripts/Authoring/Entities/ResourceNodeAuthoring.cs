using Shared;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Authoring
{
    public class ResourceNodeAuthoring : MonoBehaviour
    {
        [Header("Grid Size (그리드 셀 단위)")]
        [Min(1)] public int width = 2;
        [Min(1)] public int length = 2;
        public float height = 1f;

        [Header("Obstacle Radius (상호작용/도착/채집 판정, 월드 단위)")]
        [FormerlySerializedAs("interactionRadius")]
        [Min(0.1f)] public float obstacleRadius = 1.5f;

        [Header("Gathering Settings")]
        public ResourceType resourceType = ResourceType.Cheese;
        [Min(1)] public int amountPerGather = 10;
        [Min(1f)] public float baseGatherDuration = 1.0f;

        public class Baker : Baker<ResourceNodeAuthoring>
        {
            public override void Bake(ResourceNodeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // 태그
                AddComponent(entity, new ResourceNodeTag());
                
                // 자원 노드 Status
                AddComponent(entity, new ResourceNodeSetting
                {
                    ResourceType = authoring.resourceType,
                    AmountPerGather = authoring.amountPerGather,
                    BaseGatherDuration = authoring.baseGatherDuration,
                });
                
                // 자원 노드 상태 (점유 정보)
                AddComponent(entity, new ResourceNodeState
                {
                    OccupyingWorker = Entity.Null,
                });
                
                // 크기 (풋프린트)
                AddComponent(entity, new StructureFootprint
                {
                    Width = authoring.width,
                    Length = authoring.length,
                    Height = authoring.height
                });

                // 그리드 위치
                AddComponent(entity, new GridPosition { Position = int2.zero });

                // GridObstacle 경로 탐색 장애물 (서버 전용)
                // ObstacleGridInitSystem에서 활성화됨
                AddComponent(entity, new NeedsNavMeshObstacle());
                SetComponentEnabled<NeedsNavMeshObstacle>(entity, false); // 초기 비활성화

                // Selection Ring 크기 결정용
                AddComponent(entity, new ObstacleRadius { Radius = authoring.obstacleRadius });
            }
        }
    }
}
