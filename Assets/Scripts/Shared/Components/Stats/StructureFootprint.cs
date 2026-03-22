using Unity.Entities;

namespace Shared
{
    // 건물이 차지하는 크기 정보
    public struct StructureFootprint : IComponentData
    {
        // 그리드 칸 수 — 건설/점유 시스템용 (배치 풋프린트)
        public int Width;
        public int Length;
        public float Height;

        // 경로탐색 풋프린트 — FlowField passability용
        // 배치 풋프린트의 중앙에 위치 (오프셋 자동 계산)
        // 벽: PathWidth < Width (반투과), 나머지: PathWidth = Width (완전 차단)
        public int PathWidth;
        public int PathLength;

        // GridObstacle 밀어내기용 실제 월드 크기
        public float WorldWidth;
        public float WorldLength;
        public float WorldHeight;

        // 원형 장애물 지원 (GridObstacle 밀어내기)
        public bool IsCircular;
        public float WorldRadius;
    }
}