using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// Enemy/Turret의 공격 대상 추적용 컴포넌트
    /// 자동 타겟팅 시스템(EnemyTargetSystem)에서 갱신
    /// </summary>
    [GhostComponent]
    public struct AggroTarget : IComponentData
    {
        [GhostField] public Entity TargetEntity;
        // 마지막으로 확인한 타겟 위치
        // 타겟이 이동해도 이 좌표를 기반으로 이동
        public float3 LastTargetPosition;
    }
}
