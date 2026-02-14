using Unity.Entities;

namespace Shared
{
    public struct CommandMarkerPrefabRef : IComponentData
    {
        public Entity MoveMarkerPrefab;
        public Entity AttackMarkerPrefab;
        public Entity GatherMarkerPrefab;
    }
}
