using Unity.Entities;

namespace Shared
{
    // [GhostComponent] 불필요
    // (프리팹 데이터는 클라/서버가 이미 똑같이 갖고 시작하므로 동기화 안 해도 됨)
    public struct ProductionCost : IComponentData
    {
        public int Cost;
        public int PopulationCost; // 인구수
    }
}