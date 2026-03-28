using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// Steering 회피 방향 캐시. TimeSlice로 N프레임에 1회만 계산하고 나머지는 재사용.
    /// </summary>
    public struct CachedAvoidanceDir : IComponentData
    {
        public float3 Direction;
        public float Strength;
    }
}
