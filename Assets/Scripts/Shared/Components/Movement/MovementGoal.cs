using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 유닛의 경로 탐색 목표
    /// </summary>
    [GhostComponent]
    public struct MovementGoal : IComponentData
    {
        // 최종 목적지 [클라, 서버 둘다 사용]
        [GhostField] public float3 Destination;
        // 새 경로 계산이 필요한지 여부 [서버만 사용]
        public bool IsPathDirty;
        // 현재 따라가는 웨이포인트 인덱스 [서버만 사용]
        public byte CurrentWaypointIndex;
        //총 웨이포인트 개수 [서버만 사용]
        public byte TotalWaypoints;
        // 경로가 목적지에 도달하지 못함 (partial path) [서버만 사용]
        public bool IsPathPartial;
        // 목적지 설정 시간 (Partial Path 재시도용) [서버만 사용]
        public float DestinationSetTime;
        // 마지막 위치 체크 시점의 위치 (stuck 감지용) [서버만 사용]
        public float3 LastPositionCheck;
        // 마지막 위치 체크 시간 (stuck 감지용) [서버만 사용]
        public float LastPositionCheckTime;
    }
}
