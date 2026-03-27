using Unity.Entities;
using Unity.Mathematics;
using Shared;

namespace Client
{
    [InternalBufferCapacity(8)]
    public struct SoundEvent : IBufferElementData
    {
        public SoundType Type;
        public float3 Position;
        public float Volume;
    }
}
