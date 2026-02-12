using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;

namespace Shared
{
    /// <summary>
    /// Unity Physics의 PhysicsVelocity Ghost Variant
    /// - Linear, Angular 모두 동기화 안 함 (Quantization=0)
    /// - Kinematic 이동 방식으로 PhysicsVelocity는 서버에서만 사용
    /// </summary>
    [GhostComponentVariation(typeof(PhysicsVelocity), "PhysicsVelocity - Synced")]
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct PhysicsVelocityGhostOverride
    {
        /// <summary>선형 속도 (m/s) - 동기화 안 함 (Quantization=0, 서버 전용)</summary>
        [GhostField(Quantization = 0)]
        public float3 Linear;

        /// <summary>각속도 (rad/s) - 동기화 안 함 (Quantization=0)</summary>
        [GhostField(Quantization = 0)]
        public float3 Angular;
    }
}
