using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Enemy의 추적 관련 설정을 담는 컴포넌트
    /// </summary>
    public struct EnemyFollowConfig : IComponentData
    {
        /// <summary>Enemy 이동 속도</summary>
        public float MoveSpeed;

        /// <summary>이 거리보다 멀어지면 현재 타겟을 잃는다</summary>
        public float LoseTargetDistance;
    }
}
