using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// Enemy의 현재 추적 타겟 정보를 저장하는 컴포넌트
    /// </summary>
    public struct EnemyTarget : IComponentData
    {
        /// <summary>현재 추적 중인 대상 Entity</summary>
        public Entity TargetEntity;

        /// <summary>
        /// 마지막으로 확인한 타겟 위치
        /// 타겟이 이동해도 EnemyMoveSystem이 이 좌표를 기반으로 이동함
        /// </summary>
        public float3 LastKnownPosition;

        /// <summary>현재 유효한 타겟이 존재하는지 여부</summary>
        public bool HasTarget;
    }
}
