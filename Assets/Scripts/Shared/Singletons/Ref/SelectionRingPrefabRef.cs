using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Selection Ring 프리팹 참조 싱글톤 (팀별 프리팹)
    /// </summary>
    public struct SelectionRingPrefabRef : IComponentData
    {
        public Entity RingPrefab;       // 기본 (deprecated, 하위 호환용)
        public Entity AllyRingPrefab;   // 아군 (초록)
        public Entity EnemyRingPrefab;  // 적 (빨강)
        public Entity NeutralRingPrefab; // 중립 (노랑)
    }
}
