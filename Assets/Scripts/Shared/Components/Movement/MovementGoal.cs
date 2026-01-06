using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 유닛의 경로 탐색 상태
    /// - FinalDestination: 최종 목적지 (UI/디버그용)
    /// - NeedsPath: 새 경로 계산이 필요한지 여부
    /// - CurrentWaypointIndex/TotalWaypoints: 서버 로컬 (동기화 안함)
    /// </summary>
    [GhostComponent]
    public struct MovementGoal : IComponentData
    {
        /// <summary>최종 목적지 (경로 끝점)</summary>
        [GhostField] public float3 FinalDestination;

        /// <summary>새 경로 계산 요청 플래그</summary>
        [GhostField] public bool NeedsPath;

        /// <summary>현재 따라가는 웨이포인트 인덱스 (서버 로컬)</summary>
        public byte CurrentWaypointIndex;

        /// <summary>총 웨이포인트 개수 (서버 로컬)</summary>
        public byte TotalWaypoints;
    }
}
