using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Selection Ring 프리팹 참조 싱글톤
    /// </summary>
    public struct SelectionRingPrefabRef : IComponentData
    {
        public Entity RingPrefab;
    }
}
