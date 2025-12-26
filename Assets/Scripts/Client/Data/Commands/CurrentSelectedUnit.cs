using Unity.Entities;

namespace Client
{
    public struct CurrentSelectedUnit : IComponentData
    {
        public Entity SelectedEntity;
        public bool HasSelection;
    }
}
