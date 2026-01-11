using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;

namespace Shared
{
    // Enemy를 LastTargetPosition 방향으로 이동시키는 시스템
    // [비활성화] NavMesh 기반 PredictedMovementSystem으로 대체됨
    [DisableAutoCreation]
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct EnemyMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            float unitRadius = 0.5f;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var collisionWorld = physicsWorld.CollisionWorld;

            // Enemy(Layer 12) → Structure(Layer 7) 충돌 필터
            var collisionFilter = new CollisionFilter
            {
                BelongsTo = 1u << 12,    // Enemy
                CollidesWith = 1u << 7,
                GroupIndex = 0
            };

            // EnemyTag 필터 + EnemyState 참조 추가
            foreach (var (transform, target, chaseDistance, moveSpeed, enemyState) in
                SystemAPI.Query<
                    RefRW<LocalTransform>,
                    RefRO<AggroTarget>,
                    RefRO<EnemyChaseDistance>,
                    RefRO<MovementSpeed>,
                    RefRO<EnemyState>>()
                    .WithAll<EnemyTag>())
            {
                // EnemyState 기반 이동 조건 검사
                var currentState = enemyState.ValueRO.CurrentState;

                // Chasing 또는 Moving 상태에서만 이동
                if (currentState != EnemyContext.Chasing && currentState != EnemyContext.Wandering)
                    continue;

                // 타겟이 없으면 이동하지 않음 (Entity.Null 체크)
                if (target.ValueRO.TargetEntity == Entity.Null)
                    continue;

                float3 currentPos = transform.ValueRO.Position;
                float3 targetPos = target.ValueRO.LastTargetPosition;
                targetPos.y = currentPos.y; // 수평면 이동

                float3 dir = targetPos - currentPos;

                // 거의 같은 위치면 이동하지 않음
                if (math.lengthsq(dir) < 0.0001f)
                    continue;

                float3 direction = math.normalize(dir);
                float moveStep = moveSpeed.ValueRO.Value * dt;
                float3 finalMovement = direction * moveStep;

                // 충돌 감지 (RayCast)
                float castDistance = moveStep + unitRadius;
                var raycastInput = new RaycastInput
                {
                    Start = currentPos,
                    End = currentPos + direction * castDistance,
                    Filter = collisionFilter
                };

                if (collisionWorld.CastRay(raycastInput, out RaycastHit hit))
                {
                    float hitDistance = hit.Fraction * castDistance;

                    // 유닛 반지름보다 가까우면 충돌
                    if (hitDistance < unitRadius + moveStep)
                    {
                        // 충돌 시 슬라이딩 계산
                        float3 hitNormal = hit.SurfaceNormal;
                        hitNormal.y = 0;
                        hitNormal = math.normalizesafe(hitNormal);

                        // 슬라이딩 방향 = 원래 방향에서 법선 방향 성분 제거
                        float3 slideDirection = direction - math.dot(direction, hitNormal) * hitNormal;

                        if (math.lengthsq(slideDirection) > 0.001f)
                        {
                            slideDirection = math.normalize(slideDirection);
                            finalMovement = slideDirection * moveStep;

                            // 슬라이딩 방향으로 다시 충돌 체크
                            var slideRayInput = new RaycastInput
                            {
                                Start = currentPos,
                                End = currentPos + slideDirection * castDistance,
                                Filter = collisionFilter
                            };

                            if (collisionWorld.CastRay(slideRayInput, out RaycastHit slideHit))
                            {
                                float slideHitDist = slideHit.Fraction * castDistance;
                                if (slideHitDist < unitRadius + moveStep)
                                {
                                    float safeMove = math.max(0, slideHitDist - unitRadius);
                                    finalMovement = slideDirection * safeMove;
                                }
                            }
                        }
                        else
                        {
                            // 슬라이딩 불가 (정면 충돌) - 이동 안함
                            finalMovement = float3.zero;
                        }
                    }
                }

                transform.ValueRW.Position += finalMovement;
            }
        }
    }
}