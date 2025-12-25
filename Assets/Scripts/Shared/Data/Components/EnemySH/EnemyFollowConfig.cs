using Unity.Entities;

// Enemy의 추적 관련 설정 값들을 담는 컴포넌트
public struct EnemyFollowConfig : IComponentData
{
    // Enemy 이동 속도
    public float MoveSpeed;

    // 이 거리보다 멀어지면 기존 타겟을 포기함
    public float LoseTargetDistance;
}
