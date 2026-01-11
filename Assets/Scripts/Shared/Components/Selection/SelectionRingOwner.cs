using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Selection Ring이 어떤 부모 엔티티에 속하는지 추적
    /// </summary>
    public struct SelectionRingOwner : IComponentData
    {
        public Entity OwnerEntity;
    }
}
