using Unity.Entities;
using Unity.Mathematics;
using Shared;

namespace Client
{
    /// <summary>
    /// 사운드 이벤트 버퍼 (SoundEventEmitSystem이 추가, SoundManager가 소비).
    /// SoundEventState 싱글톤 엔티티에 부착.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct SoundEvent : IBufferElementData
    {
        public SoundType Type;
        public float3 Position;
        public float Volume;
    }
}
