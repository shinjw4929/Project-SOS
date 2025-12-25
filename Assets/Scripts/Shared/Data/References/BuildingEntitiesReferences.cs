using Unity.Entities;

namespace Shared
{
    public struct BuildingEntitiesReferences : IComponentData
    {
        public Entity wallPrefabEntity;
        public Entity barracksPrefabEntity;
    }
}
