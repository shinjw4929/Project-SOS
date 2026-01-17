using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 이동 도착 판정 시스템
    /// - 거리 + 속도 조건으로 도착 판정
    /// - 서버에서만 실행 (클라이언트는 Ghost 보간)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMovementSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct MovementArrivalSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // [최적화] EnabledRefRW를 사용하여 현재 활성화된(이동중인) 유닛만 검사
            foreach (var (destination, transform, obstacle, velocity, enabledRef) in
                     SystemAPI.Query<
                             RefRO<MovementWaypoints>,
                             RefRO<LocalTransform>,
                             RefRO<ObstacleRadius>,
                             RefRW<PhysicsVelocity>,
                             EnabledRefRW<MovementWaypoints>>())
            {
                float3 targetPos = destination.ValueRO.Current;
                targetPos.y = transform.ValueRO.Position.y;

                // 도착 범위 계산 (지정된 값이 없으면 유닛 크기 + 여유분)
                float arrivalRadius = destination.ValueRO.ArrivalRadius > 0
                    ? destination.ValueRO.ArrivalRadius
                    : obstacle.ValueRO.Radius + 0.1f;

                // [최적화] math.distancesq 사용 (제곱근 연산 제거)
                float3 diff = transform.ValueRO.Position - targetPos;
                float distanceSq = math.lengthsq(diff);
                float arrivalRadiusSq = arrivalRadius * arrivalRadius;

                // 현재 속도 체크
                float currentSpeed = math.length(velocity.ValueRO.Linear);

                // 도착 조건:
                // 1. 거리가 반경 이내이고
                // 2. 더 이상 갈 다음 웨이포인트가 없을 때 (최종 목적지)
                // 3. 속도가 충분히 느릴 때 (PhysicsVelocity 기반)
                if (distanceSq < arrivalRadiusSq && !destination.ValueRO.HasNext && currentSpeed < 0.05f)
                {
                    // 도착 완료! 컴포넌트 비활성화
                    enabledRef.ValueRW = false;

                    // 속도 완전 정지 (잔여 속도 제거)
                    velocity.ValueRW.Linear = float3.zero;
                }
            }
        }
    }
}
