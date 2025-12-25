using Unity.Entities;

namespace Shared
{
    public struct BaseStats : IComponentData
    {
        public float baseMoveSpeed;
        public float baseAttackPower;
    }
}
