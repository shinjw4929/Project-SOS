using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Mathematics;
using UnityEngine;
using Shared; // UnitTag, StructureTag, Selected

namespace Client
{
    [BurstCompile]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SelectionVisualizationSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 색상 변경 로직
            // [변경] Unit, Structure -> UnitTag, StructureTag
            foreach (var (baseColor, selected, entity) in SystemAPI.Query<
                             RefRW<URPMaterialPropertyBaseColor>,
                             RefRO<Selected>>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState) // 비활성화된 Selected도 읽기 위해
                         .WithEntityAccess())
            {
                bool isSelected = state.EntityManager.IsComponentEnabled<Selected>(entity);
                
                // 현재 색상
                float4 currentColor = baseColor.ValueRO.Value;
                
                // 목표 색상 (선택됨: 파랑, 아니면: 흰색)
                // * 만약 팀 컬러가 이미 적용되어 있다면, Emission을 조절하거나 
                // * Selection Circle(데칼)을 켜는 방식을 권장하지만, 일단 요청대로 색상 변경 유지
                float4 targetColor = isSelected ? new float4(0f, 0f, 1f, 1f) : new float4(1f, 1f, 1f, 1f);

                // 색상이 다를 때만 변경 (성능 최적화)
                if (!math.all(currentColor == targetColor))
                {
                    baseColor.ValueRW.Value = targetColor;
                }
            }
        }
    }
}