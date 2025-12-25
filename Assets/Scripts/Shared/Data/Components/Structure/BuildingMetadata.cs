using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 건물의 메타데이터 (크기, 비용 등)
    /// 건물 프리팹에 추가하여 데이터 주도 방식으로 관리
    /// </summary>
    public struct BuildingMetadata : IComponentData
    {
        /// <summary>그리드 너비 (칸 수)</summary>
        public int width;

        /// <summary>그리드 높이 (칸 수)</summary>
        public int height;

        /// <summary>건설 비용 (향후 확장용)</summary>
        public int cost;

        /// <summary>건설 시간 초 (향후 확장용)</summary>
        public float buildTime;
    }
}
