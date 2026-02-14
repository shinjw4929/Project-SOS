using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    public struct CameraPositionRpc : IRpcCommand
    {
        public float3 Position;
        public float2 ViewHalfExtent; // 뷰포트 반크기 (X, Z)
    }
}
