using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 적 타입 열거형.
    /// </summary>
    public enum EnemyType : byte
    {
        Small = 0,
        Big = 1,
        Flying = 2
    }

    /// <summary>
    /// 적 프리팹 카탈로그 싱글톤.
    /// 각 적 타입별 프리팹을 명시적 필드로 참조하여 순서 종속성 제거.
    /// </summary>
    public struct EnemyPrefabCatalog : IComponentData
    {
        public Entity SmallPrefab;
        public Entity BigPrefab;
        public Entity FlyingPrefab;

        /// <summary>
        /// EnemyType으로 프리팹 Entity 조회
        /// </summary>
        public readonly Entity GetPrefab(EnemyType type)
        {
            return type switch
            {
                EnemyType.Small => SmallPrefab,
                EnemyType.Big => BigPrefab,
                EnemyType.Flying => FlyingPrefab,
                _ => Entity.Null
            };
        }
    }
}
