using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 투사체 시각 효과 RPC
    /// - 서버에서 클라이언트로 전송
    /// - 클라이언트에서 시각적 투사체 스폰
    /// - 데미지는 서버에서 이미 적용됨 (필중)
    /// </summary>
    public struct ProjectileVisualRpc : IRpcCommand
    {
        public float3 StartPosition;
        public float3 TargetPosition;
    }
}
