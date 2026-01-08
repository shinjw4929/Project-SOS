using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 경로 웨이포인트 버퍼 (서버 전용)
    /// 클라이언트로 동기화하지 않음 - 대역폭 최적화
    /// 클라이언트는 MoveTarget.position(현재 웨이포인트)만 동기화받음
    /// </summary>
    public struct PathWaypoint : IBufferElementData
    {
        public float3 Position;
    }
}
