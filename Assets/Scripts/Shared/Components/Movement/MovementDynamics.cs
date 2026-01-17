using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 가속도/감속도 기반 이동 파라미터
    /// - PhysicsVelocity 기반 이동 시스템에서 사용
    /// - 버프/디버프 시 Ghost 동기화
    /// </summary>
    [GhostComponent]
    public struct MovementDynamics : IComponentData
    {
        /// <summary>최대 이동 속도 (m/s)</summary>
        [GhostField(Quantization = 10)]
        public float MaxSpeed;

        /// <summary>가속도 (m/s^2)</summary>
        [GhostField(Quantization = 10)]
        public float Acceleration;

        /// <summary>감속도 (m/s^2)</summary>
        [GhostField(Quantization = 10)]
        public float Deceleration;

        /// <summary>회전 속도 (rad/s)</summary>
        [GhostField(Quantization = 10)]
        public float RotationSpeed;
    }
}
