using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Mathematics;
using Shared;

namespace Client
{
    /// <summary>
    /// Worker 가시성 시스템 (클라이언트)
    /// - IsInsideNode == true면 렌더링 비활성화 (머터리얼 알파값 조절)
    /// - SelectionVisualizationSystem 이전에 실행되어 알파값만 처리
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct WorkerVisibilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
        }

        public void OnUpdate(ref SystemState state)
        {
            foreach (var (workerState, baseColor, entity)
                in SystemAPI.Query<RefRO<WorkerState>, RefRW<URPMaterialPropertyBaseColor>>()
                    .WithAll<WorkerTag>()
                    .WithEntityAccess())
            {
                bool isInsideNode = workerState.ValueRO.IsInsideNode;
                float4 currentColor = baseColor.ValueRO.Value;

                // 노드 내부면 투명하게, 아니면 원래 알파값 유지
                float targetAlpha = isInsideNode ? 0.0f : 1.0f;

                // 알파값만 변경 (RGB 색상은 유지 - SelectionVisualizationSystem에서 처리)
                if (math.abs(currentColor.w - targetAlpha) > 0.01f)
                {
                    baseColor.ValueRW.Value = new float4(currentColor.xyz, targetAlpha);
                }
            }
        }
    }
}
