using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// CarriedResource 엔티티가 어떤 Worker에 속하는지 추적
    /// Ghost 동기화를 위해 별도 컴포넌트 사용 (Parent는 동기화 안됨)
    /// </summary>
    [GhostComponent]
    public struct CarriedResourceOwner : IComponentData
    {
        [GhostField]
        public Entity WorkerEntity;
    }
}
