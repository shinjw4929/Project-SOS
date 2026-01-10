using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Shared;
using UnityEngine;

namespace Client
{
    /// <summary>
    /// 자원 운반 시각화 시스템 (클라이언트)
    /// - CarriedAmount > 0이면 Worker 위에 자원 오브젝트 표시
    /// - 간단한 구현: 머터리얼 색상 변경으로 표시 (비선택 상태일 때만)
    /// - 선택 상태일 때는 SelectionVisualizationSystem의 파란색 유지
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class CarriedResourceVisualizationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            // 현재는 색상 변경으로 간단히 표현
            // 선택되지 않은 Worker만 자원 보유 색상 적용
            foreach (var (workerState, baseColor, entity)
                in SystemAPI.Query<RefRO<WorkerState>, RefRW<Unity.Rendering.URPMaterialPropertyBaseColor>>()
                    .WithAll<WorkerTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                    .WithEntityAccess())
            {
                // 노드 내부면 이미 투명하므로 건너뛰기
                if (workerState.ValueRO.IsInsideNode) continue;

                // 선택 상태면 SelectionVisualizationSystem의 색상 유지
                bool isSelected = EntityManager.HasComponent<Selected>(entity) &&
                                  EntityManager.IsComponentEnabled<Selected>(entity);
                if (isSelected) continue;

                bool hasResource = workerState.ValueRO.CarriedAmount > 0;
                float4 currentColor = baseColor.ValueRO.Value;

                // 자원을 들고 있으면 노란색 틴트, 아니면 흰색
                float4 targetColor;
                if (hasResource)
                {
                    // 자원 종류에 따라 색상 변경
                    switch (workerState.ValueRO.CarriedType)
                    {
                        case ResourceType.Cheese:
                            targetColor = new float4(1.0f, 0.9f, 0.3f, 1.0f); // 노란색 (광물)
                            break;
                        default:
                            targetColor = new float4(1.0f, 1.0f, 1.0f, 1.0f); // 흰색
                            break;
                    }
                }
                else
                {
                    targetColor = new float4(1.0f, 1.0f, 1.0f, 1.0f); // 흰색
                }

                // 색상이 다르면 업데이트
                if (math.any(math.abs(currentColor - targetColor) > 0.01f))
                {
                    baseColor.ValueRW.Value = targetColor;
                }
            }
        }
    }
}
