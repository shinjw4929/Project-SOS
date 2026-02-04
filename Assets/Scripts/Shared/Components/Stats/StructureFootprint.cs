using Unity.Entities;

namespace Shared
{
    // 건물이 차지하는 크기 정보
    public struct StructureFootprint : IComponentData
    {
        // 그리드 칸 수 (건설/점유 시스템용)
        public int Width;  // 가로 칸 수
        public int Length; // 세로 칸 수
        public float Height; // 건물 높이

        // NavMeshObstacle용 실제 월드 크기
        public float WorldWidth;  // 실제 가로 크기
        public float WorldLength; // 실제 세로 크기
        public float WorldHeight; // NavMeshObstacle 높이

        // 원형 장애물 지원 (NavMeshObstacle Capsule 형태)
        public bool IsCircular;   // 원형 여부 (true면 Capsule, false면 Box)
        public float WorldRadius; // 원형일 때 반지름
    }
}