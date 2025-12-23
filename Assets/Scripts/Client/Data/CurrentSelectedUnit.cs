using Unity.Entities;

namespace Client
{
    public struct CurrentSelectedUnit : IComponentData
    {
        public Entity selectedEntity;
        public bool hasSelection;
    }
}
