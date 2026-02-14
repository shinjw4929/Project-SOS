using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    /// <summary>
    /// 미니맵 적 위치 데이터 싱글톤.
    /// Double buffer: EnemyPositions(렌더링 읽기) / PendingPositions(수신 쓰기) 스왑 패턴.
    /// MinimapDataReceiveSystem이 OnCreate에서 생성, OnDestroy에서 Dispose.
    /// </summary>
    public struct MinimapDataState : IComponentData
    {
        public NativeList<float2> EnemyPositions;
        public NativeList<float2> PendingPositions;
        public uint PendingFrameId;
        public int ReceivedCount;
        public ushort ExpectedTotalCount;
    }
}
