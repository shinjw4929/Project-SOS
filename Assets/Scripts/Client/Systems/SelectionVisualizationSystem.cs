using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Mathematics;
using UnityEngine;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SelectionVisualizationSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 색상 변경 로직
            foreach (var (baseColor, selected, entity) in SystemAPI.Query<
                             RefRW<URPMaterialPropertyBaseColor>,
                             RefRO<Shared.Selected>>()
                         .WithAll<Player>() // Player 태그가 있는 것만
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState) // 비활성화된 Selected도 읽기 위해
                         .WithEntityAccess())
            {
                bool isSelected = state.EntityManager.IsComponentEnabled<Shared.Selected>(entity);
                
                // 현재 색상
                float4 currentColor = baseColor.ValueRO.Value;
                
                // 목표 색상 (선택됨: 파랑, 아니면: 흰색)
                float4 targetColor = isSelected ? new float4(0f, 0f, 1f, 1f) : new float4(1f, 1f, 1f, 1f);

                // 색상이 다를 때만 변경 (성능 최적화 & 로그 스팸 방지)
                if (!math.all(currentColor == targetColor))
                {
                    baseColor.ValueRW.Value = targetColor;
                }
            }
        }
    }
}