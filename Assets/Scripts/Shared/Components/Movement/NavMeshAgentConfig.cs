using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// NavMesh Agent Type 설정
    /// Unity Navigation 창에서 정의한 Agent Type 인덱스를 저장
    /// PathfindingSystem에서 유닛 크기별로 다른 경로 계산에 사용
    /// </summary>
    public struct NavMeshAgentConfig : IComponentData
    {
        /// <summary>
        /// Unity Navigation Agents 탭에서의 순서 (0부터 시작)
        /// 0 = 첫 번째 Agent Type (보통 Humanoid)
        /// 1 = 두 번째 Agent Type
        /// 2 = 세 번째 Agent Type
        /// </summary>
        public int AgentTypeIndex;
    }
}