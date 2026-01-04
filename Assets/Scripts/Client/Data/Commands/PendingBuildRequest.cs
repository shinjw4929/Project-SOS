using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    /// <summary>
    /// 이동 후 건설을 위한 대기 중인 건설 요청 (클라이언트 전용)
    /// - 사거리 밖에서 건설 명령 시 유닛에 부착
    /// - 이동 완료 후 BuildRequestRpc 전송
    /// 네트워크 동기화 불필요 (클라이언트 로컬 상태)
    /// </summary>
    public struct PendingBuildRequest : IComponentData
    {
        public int StructureIndex;        // 건설할 건물 인덱스
        public int2 GridPosition;         // 건설 위치 (그리드 좌표)
        public float3 BuildSiteCenter;    // 건설 위치 (월드 좌표, 이동 목표용)
        public float RequiredRange;       // 건설 사거리 (도착 판정용)
        public int Width;                 // 건물 너비 (AABB 계산용)
        public int Length;                // 건물 길이 (AABB 계산용)
        public float StructureRadius;     // 건물 반지름 (ObstacleRadius, 도착 판정용)
    }
}
