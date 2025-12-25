using Unity.Entities;
using Unity.Mathematics;

// Enemy가 현재 추적 중인 타겟 정보를 저장하는 컴포넌트
public struct EnemyTarget : IComponentData
{
    // 현재 추적 중인 대상 Entity
    public Entity TargetEntity;

    // 마지막으로 확인한 타겟 위치
    // 타겟이 이동해도 EnemyMoveSystem이 이 좌표를 기준으로 이동함
    public float3 LastKnownPosition;

    // 현재 유효한 타겟을 가지고 있는지 여부
    public bool HasTarget;
}
