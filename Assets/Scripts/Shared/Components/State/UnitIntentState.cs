using Unity.Entities;
using Unity.NetCode;

namespace Shared

{
    /// <summary>
    /// 유닛의 목표 = 사용자의 의도를 나타내는 State
    /// - AI 및 명령 처리 시스템이 참조
    /// </summary>

    [GhostComponent]
    public struct UnitIntentState : IComponentData
    {
        public Intent State;
        public Entity TargetEntity;
    }

    public enum Intent : byte
    {
        None = 0,
        Hold = 1,
        Patrol = 2,
        Build = 3,
        Gather = 4,
        Attack = 5,
    }
}