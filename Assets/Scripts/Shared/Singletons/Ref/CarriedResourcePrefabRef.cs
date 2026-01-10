using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Worker가 운반하는 자원 시각화용 프리팹 참조 싱글톤
    /// - 베이킹: CarriedResourcePrefabRefAuthoring
    /// - 사용: WorkerGatheringSystem에서 자원 엔티티 생성 시
    /// </summary>
    public struct CarriedResourcePrefabRef : IComponentData
    {
        public Entity CheesePrefab;
    }
}
