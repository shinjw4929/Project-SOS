using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared

{
    /// <summary>
    /// 유닛의 행동 의도
    /// - 서버가 판단한 "무엇을 하려는가"를 표현
    /// </summary>

    [GhostComponent]
    public struct UnitIntentState : IComponentData
    {
        [GhostField] public Intent State;
        [GhostField] public Entity TargetEntity;
        [GhostField] public float3 TargetLastKnownPos;
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