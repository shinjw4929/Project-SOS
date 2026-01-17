using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 건설 이동 명령 RPC
    /// - 사거리 밖에서 건설 시 사용
    /// - 서버에서 이동 + 도착 시 건설 처리
    /// </summary>
    public struct BuildMoveRequestRpc : IRpcCommand
    {
        public int BuilderGhostId;      // 빌더 유닛 GhostId
        public int StructureIndex;      // 건설할 건물 인덱스
        public int2 GridPosition;       // 건설 위치 (그리드 좌표)
        public float3 MoveTarget;       // 이동 목표 위치
        public float3 BuildSiteCenter;  // 건물 중심 (도착 판정용)
        public float StructureRadius;   // 건물 반지름 (도착 판정용)
    }
}
