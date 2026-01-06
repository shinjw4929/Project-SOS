using Unity.Entities;

namespace Shared
{
    public struct EnemyPrefabRef : IComponentData
    {
        public Entity Prefab;
    }
}
