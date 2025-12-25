using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct RTSInputState : IComponentData
    {
        public float3 targetPosition;
        public bool hasTarget;
    }
}