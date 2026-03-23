using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 건물/자원이 차지하는 그리드 크기 정보.
    /// 그리드 셀이 단일 소스: 경로 차단은 max(1, Width-2)로 파생, push-out은 Width×CellSize로 파생.
    /// </summary>
    public struct StructureFootprint : IComponentData
    {
        /// <summary>배치 폭 (그리드 셀 단위)</summary>
        public int Width;
        /// <summary>배치 길이 (그리드 셀 단위)</summary>
        public int Length;
        /// <summary>건물 높이 (월드 단위, m). BuildArrivalSystem에서 위치 계산에 사용.</summary>
        public float Height;
    }
}