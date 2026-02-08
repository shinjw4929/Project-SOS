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
    /// - 거리 조건으로 도착 판정 (마지막 웨이포인트 + 반경 이내)
    /// - 2차 판정: 확장 반경 내에서 Separation에 의해 밀려나는 경우 도착 처리
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
            // 적 이동 도착 처리 (Intent 없음)
            foreach (var (destination, transform, obstacle, velocity, enabledRef) in
                     SystemAPI.Query<
                             RefRO<MovementWaypoints>,
                             RefRO<LocalTransform>,
                             RefRO<ObstacleRadius>,
                             RefRW<PhysicsVelocity>,
                             EnabledRefRW<MovementWaypoints>>()
                     .WithAll<EnemyTag>())
            {
                if (CheckArrival(destination.ValueRO, transform.ValueRO, obstacle.ValueRO, velocity.ValueRO))
                {
                    enabledRef.ValueRW = false;
                    velocity.ValueRW.Linear = float3.zero;
                }
            }

            // 유닛 이동 도착 처리 (Intent 변경 필요)
            foreach (var (destination, transform, obstacle, velocity, intentState, enabledRef) in
                     SystemAPI.Query<
                             RefRO<MovementWaypoints>,
                             RefRO<LocalTransform>,
                             RefRO<ObstacleRadius>,
                             RefRW<PhysicsVelocity>,
                             RefRW<UnitIntentState>,
                             EnabledRefRW<MovementWaypoints>>()
                     .WithAll<UnitTag>())
            {
                if (CheckArrival(destination.ValueRO, transform.ValueRO, obstacle.ValueRO, velocity.ValueRO))
                {
                    enabledRef.ValueRW = false;
                    velocity.ValueRW.Linear = float3.zero;

                    // Intent.Move 상태였다면 Idle로 전환 (자동 타겟팅 활성화)
                    if (intentState.ValueRO.State == Intent.Move)
                    {
                        intentState.ValueRW.State = Intent.Idle;
                        intentState.ValueRW.TargetEntity = Entity.Null;
                    }
                }
            }
        }

        /// <summary>
        /// 도착 조건 체크
        /// - 1차: 도착 반경 이내 + 마지막 웨이포인트
        /// - 2차: 확장 반경(2배) 이내 + 목적지 방향으로 이동하지 않는 경우
        /// </summary>
        [BurstCompile]
        private static bool CheckArrival(
            in MovementWaypoints destination,
            in LocalTransform transform,
            in ObstacleRadius obstacle,
            in PhysicsVelocity velocity)
        {
            if (destination.HasNext)
                return false;

            float3 targetPos = destination.Current;
            targetPos.y = transform.Position.y;

            // 도착 범위 계산 (지정된 값이 없으면 유닛 크기 + 여유분)
            float arrivalRadius = destination.ArrivalRadius > 0
                ? destination.ArrivalRadius
                : obstacle.Radius + 0.1f;

            float3 diff = transform.Position - targetPos;
            float distanceSq = math.lengthsq(diff);
            float arrivalRadiusSq = arrivalRadius * arrivalRadius;

            // 1차 판정: 도착 반경 이내
            if (distanceSq < arrivalRadiusSq)
                return true;

            // 2차 판정: 확장 반경(2배) 이내에서 목적지 방향으로 이동하지 않는 경우
            // Separation Force가 도착 반경 진입을 방해하여 진동하는 유닛 포착
            float expandedRadiusSq = arrivalRadiusSq * 4f;
            if (distanceSq < expandedRadiusSq)
            {
                float3 toTarget = targetPos - transform.Position;
                if (math.dot(velocity.Linear, toTarget) <= 0)
                    return true;
            }

            return false;
        }
    }
}
