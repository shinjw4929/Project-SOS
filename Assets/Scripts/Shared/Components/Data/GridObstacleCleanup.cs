using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 건물 파괴 시 경로탐색 차단 해제를 위한 클린업 컴포넌트
    /// GridObstacleResponseSystem에서 부착, GridObstacleCleanupSystem에서 제거
    /// </summary>
    public struct GridObstacleCleanup : ICleanupComponentData
    {
        public int2 GridPosition;
        public int Width;       // 배치 풋프린트 (월드 좌표 복원용)
        public int Length;
        public int PathWidth;   // 경로탐색 풋프린트 (UnmarkPathBlocked용)
        public int PathLength;
    }
}
