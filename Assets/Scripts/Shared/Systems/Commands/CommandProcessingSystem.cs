using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 유닛 명령 처리 시스템
    /// - 현재 모든 명령이 RPC 시스템으로 이전됨
    /// - Move: HandleMoveRequestSystem (서버)
    /// - Attack: HandleAttackRequestSystem (서버)
    /// - Build: HandleBuildRequestSystem / HandleBuildMoveRequestSystem (서버)
    ///
    /// 향후 클라이언트 예측이 필요한 명령이 있을 경우 이 시스템에서 처리
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    partial struct CommandProcessingSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 현재 모든 명령 처리가 서버 RPC 시스템으로 이전됨
            // 클라이언트 예측이 필요한 새로운 명령 타입이 추가될 경우 여기서 처리
        }
    }
}
