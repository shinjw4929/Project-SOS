using Unity.Entities;

namespace Client
{
    /// <summary>
    /// SoundEvent 버퍼를 보유하는 싱글톤 엔티티의 마커 컴포넌트.
    /// SystemAPI.GetSingletonEntity 로 버퍼 엔티티를 찾는 데 사용.
    /// </summary>
    public struct SoundEventState : IComponentData { }
}
