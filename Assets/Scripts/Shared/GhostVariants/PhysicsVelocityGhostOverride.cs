using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;

namespace Shared
{
    /// <summary>
    /// Unity Physics의 PhysicsVelocity를 Ghost 동기화하기 위한 Variant
    /// - Linear만 동기화 (Angular는 미사용)
    /// - InterpolateAndExtrapolate로 부드러운 보정
    /// </summary>
    [GhostComponentVariation(typeof(PhysicsVelocity), "PhysicsVelocity - Synced")]
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct PhysicsVelocityGhostOverride
    {
        /// <summary>선형 속도 (m/s)</summary>
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public float3 Linear;

        /// <summary>각속도 (rad/s) - 동기화 안 함 (Quantization=0)</summary>
        [GhostField(Quantization = 0)]
        public float3 Angular;
    }
}
