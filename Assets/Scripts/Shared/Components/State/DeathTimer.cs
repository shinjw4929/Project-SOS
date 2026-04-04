using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 사망 연출 타이머. Dying 상태 진입 시 부착되며,
    /// RemainingTime이 0 이하가 되면 엔티티가 파괴된다.
    /// </summary>
    public struct DeathTimer : IComponentData
    {
        public float RemainingTime;
    }
}
