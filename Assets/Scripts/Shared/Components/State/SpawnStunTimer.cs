using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 스폰 직후 일정 시간 동안 적이 정지 상태를 유지하도록 하는 타이머.
    /// 타이머가 0이 되면 컴포넌트가 제거되고 정상 행동을 시작한다.
    /// </summary>
    public struct SpawnStunTimer : IComponentData
    {
        public float RemainingTime;
    }
}
