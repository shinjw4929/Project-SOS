using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 건물 생성 시 GridObstacle 처리 요청 태그
    /// GridObstacleResponseSystem에서 감지 → IsPathBlocked 마킹 후 비활성화
    /// </summary>
    public struct NeedsNavMeshObstacle : IComponentData, IEnableableComponent
    {
    }
}
