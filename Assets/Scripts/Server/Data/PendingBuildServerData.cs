using Unity.Entities;
using Unity.Mathematics;

namespace Server
{
    /// <summary>
    /// 서버 전용 대기 건설 데이터
    /// - 이동 후 건설을 위한 정보 저장
    /// - 도착 시 BuildArrivalSystem에서 처리
    /// </summary>
    public struct PendingBuildServerData : IComponentData
    {
        public int StructureIndex;      // 건설할 건물 인덱스
        public int2 GridPosition;       // 건설 위치 (그리드 좌표)
        public float3 BuildSiteCenter;  // 건물 중심 (도착 판정용)
        public float StructureRadius;   // 건물 반지름 (도착 판정용)
        public int OwnerNetworkId;      // 소유자 네트워크 ID
        public Entity SourceConnection; // 소스 연결 (알림용)
    }
}
