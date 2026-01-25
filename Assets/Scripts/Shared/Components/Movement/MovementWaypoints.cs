using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct MovementWaypoints : IComponentData, IEnableableComponent
    {
        [GhostField] public float3 Current;      // 지금 당장 가야 할 경유지
        [GhostField] public float3 Next;         // 그다음 꺾어야 할 경유지 (코너링/부드러운 전환용)
        [GhostField] [MarshalAs(UnmanagedType.U1)] public bool HasNext;
        [GhostField] public float ArrivalRadius; // 도착 판정용 (0이면 기본값 사용)
    }
}