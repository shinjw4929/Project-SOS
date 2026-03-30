using Unity.Entities;
using Shared;

namespace Client
{
    /// <summary>
    /// 적의 이전 프레임 EnemyContext (사운드 이벤트 변화 감지용)
    /// </summary>
    public struct PreviousEnemyContext : IComponentData
    {
        public EnemyContext Value;
        public float AttackSoundTimer;
    }
}
