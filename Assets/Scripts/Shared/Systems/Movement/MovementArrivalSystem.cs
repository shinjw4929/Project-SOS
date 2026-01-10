using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;

namespace Shared
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMovementSystem))]
    [BurstCompile]
    public partial struct MovementArrivalSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // [최적화] EnabledRefRW를 사용하여 현재 활성화된(이동중인) 유닛만 검사
            foreach (var (destination, transform, obstacle, enabledRef) in
                     SystemAPI.Query<
                             RefRO<MovementWaypoints>,
                             RefRO<LocalTransform>,
                             RefRO<ObstacleRadius>,
                             EnabledRefRW<MovementWaypoints>>() // <--- 여기 주목
                         .WithAll<Simulate>())
            {
                float3 targetPos = destination.ValueRO.Current;
                targetPos.y = transform.ValueRO.Position.y;

                // 도착 범위 계산 (지정된 값이 없으면 유닛 크기 + 여유분)
                float arrivalRadius = destination.ValueRO.ArrivalRadius > 0
                    ? destination.ValueRO.ArrivalRadius
                    : obstacle.ValueRO.Radius + 0.1f;

                float distance = math.distance(transform.ValueRO.Position, targetPos);

                // 도착 조건:
                // 1. 거리가 반경 이내이고
                // 2. 더 이상 갈 다음 웨이포인트가 없을 때 (최종 목적지)
                if (distance < arrivalRadius && !destination.ValueRO.HasNext)
                {
                    // 도착 완료! 컴포넌트 비활성화
                    // SystemAPI 호출 없이 즉시 처리됨 (매우 빠름)
                    enabledRef.ValueRW = false; 
                }
            }
        }
    }
}