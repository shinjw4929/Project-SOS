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
        /// </summary>
        [BurstCompile]
        private static bool CheckArrival(
            in MovementWaypoints destination,
            in LocalTransform transform,
            in ObstacleRadius obstacle,
            in PhysicsVelocity velocity)
        {
            float3 targetPos = destination.Current;
            targetPos.y = transform.Position.y;

            // 도착 범위 계산 (지정된 값이 없으면 유닛 크기 + 여유분)
            float arrivalRadius = destination.ArrivalRadius > 0
                ? destination.ArrivalRadius
                : obstacle.Radius + 0.1f;

            // [최적화] math.distancesq 사용 (제곱근 연산 제거)
            float3 diff = transform.Position - targetPos;
            float distanceSq = math.lengthsq(diff);
            float arrivalRadiusSq = arrivalRadius * arrivalRadius;

            // 현재 속도 체크
            float currentSpeed = math.length(velocity.Linear);

            // 도착 조건:
            // 1. 거리가 반경 이내이고
            // 2. 더 이상 갈 다음 웨이포인트가 없을 때 (최종 목적지)
            // 3. 속도가 충분히 느릴 때 (PhysicsVelocity 기반)
            return distanceSq < arrivalRadiusSq && !destination.HasNext && currentSpeed < 0.05f;
        }
    }
}
