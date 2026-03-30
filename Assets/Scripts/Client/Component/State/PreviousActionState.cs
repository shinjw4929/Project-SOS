using Unity.Entities;
using Shared;

namespace Client
{
    /// <summary>
    /// 유닛의 이전 프레임 ActionState (사운드 이벤트 변화 감지용)
    /// </summary>
    public struct PreviousActionState : IComponentData
    {
        public Action Value;
        public float AttackSoundTimer;
    }
}
