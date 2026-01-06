using Unity.Entities;
using UnityEngine;

namespace Shared
{
    /// <summary>
    /// Managed Component: NavMeshObstacle GameObject 직접 참조
    /// - FindObjectsOfType 제거하여 O(1) 접근
    /// - 서버 전용 (클라이언트는 경로 계산 안함)
    /// - 엔티티가 Destroy되어도 이 컴포넌트는 남아서 우리가 직접 지울 때까지 기다림
    /// </summary>
    public class NavMeshObstacleReference : ICleanupComponentData
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
