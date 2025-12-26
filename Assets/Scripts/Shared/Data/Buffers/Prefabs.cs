using Unity.Entities;

namespace Shared
{
    public struct UnitPrefabElement : IBufferElementData
    {
        public Entity PrefabEntity;
    }

    public struct StructurePrefabElement : IBufferElementData
    {
        public Entity PrefabEntity;
    }
}
