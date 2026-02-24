using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    /// <summary>
    /// 미니맵 적/유닛 위치 데이터 싱글톤.
    /// Double buffer: Data(렌더링 읽기) / PendingData(수신 쓰기) 스왑 패턴.
    /// float3: x=posX, y=posZ, z=teamId. teamId=-1은 적, teamId>0은 유닛.
    /// MinimapDataReceiveSystem이 OnCreate에서 생성, OnDestroy에서 Dispose.
    /// </summary>
    public struct MinimapDataState : IComponentData
    {
        public NativeList<float3> Data;
        public NativeList<float3> PendingData;
        public uint PendingFrameId;
        public int ReceivedCount;
        public ushort ExpectedTotalCount;
        public ushort EnemyCount;
        public ushort UnitCount;
    }
}
