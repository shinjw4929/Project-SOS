using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    // 공격, 작업 대상이 필요한 "모든 엔티티(유닛, 적, 포탑)"가 공유하는 컴포넌트
    [GhostComponent]
    public struct Target : IComponentData
    {
        [GhostField] public Entity TargetEntity;
        // 현재 유효한 타겟이 존재하는지 여부
        [GhostField] public bool HasTarget;
        // 마지막으로 확인한 타겟 위치
        //타겟이 이동해도 이 좌표를 기반으로 이동
        public float3 LastTargetPosition;
    }
}