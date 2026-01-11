using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 초기 배치 벽의 자동 파괴 타이머.
    /// RemainingTime이 0 이하가 되면 벽이 파괴됨.
    /// </summary>
    public struct InitialWallDecayTimer : IComponentData
    {
        public float RemainingTime;
    }
}
