using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 유닛의 기본 스탯 (로컬 전용, 네트워크 동기화 없음)
    /// Prefab에서 베이킹된 초기값을 각 클라이언트가 독립적으로 유지합니다.
    /// 런타임에 버프/디버프로 변경 가능합니다.
    /// </summary>
    public struct UnitStats : IComponentData
    {
        /// <summary>이동 속도 (units/second)</summary>
        public float moveSpeed;

        /// <summary>공격력 (데미지)</summary>
        public float attackPower;
    }
}
