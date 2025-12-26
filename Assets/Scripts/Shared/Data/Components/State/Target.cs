using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 공격, 작업 대상이 필요한 "모든 엔티티(유닛, 포탑)"가 공유하는 컴포넌트
    [GhostComponent]
    public struct Target : IComponentData
    {
        [GhostField] public Entity TargetEntity;
    }
}