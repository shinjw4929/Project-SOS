using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 유닛 간 밀어내기 힘 저장 (매 프레임 갱신)
    /// - UnitSeparationSystem이 계산
    /// - PredictedMovementSystem이 적용
    /// </summary>
    public struct SeparationForce : IComponentData
    {
        public float3 Force;
    }
}