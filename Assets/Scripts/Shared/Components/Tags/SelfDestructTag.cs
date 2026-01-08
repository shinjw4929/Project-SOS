using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 자폭이 예약된 엔티티에 부착
    /// 서버에서 SelfDestructTimerSystem이 이 태그를 처리
    /// </summary>
    [GhostComponent]
    public struct SelfDestructTag : IComponentData
    {
        /// <summary>폭발까지 남은 시간 (Delay 카운트다운)</summary>
        [GhostField] public float RemainingTime;
    }
}
