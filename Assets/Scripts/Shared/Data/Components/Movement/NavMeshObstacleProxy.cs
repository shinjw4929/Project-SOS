using Unity.Entities;
using UnityEngine;

namespace Shared
{
    /// <summary>
    /// Managed Component: NavMeshObstacle GameObject 직접 참조
    /// - FindObjectsOfType 제거하여 O(1) 접근
    /// - 서버 전용 (클라이언트는 경로 계산 안함)
    /// </summary>
    public class NavMeshObstacleReference : IComponentData
    {
        public GameObject ObstacleObject;
    }

    /// <summary>
    /// 건물 생성 시 NavMeshObstacle GameObject 생성 요청 태그
    /// </summary>
    public struct NeedsNavMeshObstacle : IComponentData, IEnableableComponent
    {
    }
}
