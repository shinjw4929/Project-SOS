using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 유닛에 경로 탐색 기능을 부여하는 Authoring 컴포넌트
    /// 유닛 프리팹에 추가하면 PathfindingState와 PathWaypoint 버퍼가 베이킹됨
    /// </summary>
    public class PathfindingAuthoring : MonoBehaviour
    {
        public class Baker : Baker<PathfindingAuthoring>
        {
            public override void Bake(PathfindingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // PathfindingState 컴포넌트 추가
                AddComponent(entity, new PathfindingState
                {
                    FinalDestination = default,
                    NeedsPath = false,
                    CurrentWaypointIndex = 0,
                    TotalWaypoints = 0
                });

                // PathWaypoint 버퍼 추가 (서버에서만 사용)
                AddBuffer<PathWaypoint>(entity);
            }
        }
    }
}
