using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    public struct CameraSettings : IComponentData
    {
        // Hero Follow 설정
        public float3 Offset;
        public float SmoothTime;
        public bool LockRotation;

        // Edge Pan 설정
        public float EdgePanSpeed;
        public float EdgeThreshold;
        public float2 MapBoundsMin;
        public float2 MapBoundsMax;
    }
}
