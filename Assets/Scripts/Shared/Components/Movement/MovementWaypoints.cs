using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct MovementWaypoints : IComponentData, IEnableableComponent
    {
        public float3 Current;      // 지금 당장 가야 할 경유지 [서버 전용]
        public float3 Next;         // 그다음 꺾어야 할 경유지 (코너링/부드러운 전환용) [서버 전용]
        [MarshalAs(UnmanagedType.U1)] public bool HasNext; // Burst blittable 필수 [서버 전용]
        public float ArrivalRadius; // 도착 판정용 (0이면 기본값 사용) [서버 전용]
    }
}