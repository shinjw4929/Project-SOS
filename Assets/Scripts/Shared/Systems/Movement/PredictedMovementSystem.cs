using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.NetCode;
using Unity.Collections;

namespace Shared
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    public partial struct PredictedMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            var job = new MoveJob
            {
                DeltaTime = deltaTime,
                CollisionWorld = physicsWorld.CollisionWorld,
                // [필터 설정] 아군/적군/건물 등 충돌 레이어 설정
                CollisionFilter = new CollisionFilter
                {
                    BelongsTo = 1u << 11 | 1u << 12, // Unit | Enemy
                    CollidesWith = 1u << 6 | 1u << 7 | 1u << 11 | 1u << 12,  // Ground | Structure | Unit | Enemy
                    GroupIndex = 0
                }
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;
        public CollisionFilter CollisionFilter;

        private const float CornerRadius = 0.5f;
        private const float WallCheckDistance = 1.0f;
        private const float RotationSpeed = 20.0f; // 회전 보간 속도

        // [최적화] IEnableableComponent인 MovementDestination을 사용하여
        // 활성화된(이동 중인) 유닛만 자동으로 처리됩니다.
        private void Execute(
            ref LocalTransform transform,
            ref MovementWaypoints waypoints,
            in MovementSpeed speed)
        {
            float3 currentPos = transform.Position;
            float3 targetPos = waypoints.Current; // 현재 가야할 웨이포인트

            // Y축 고정 (RTS는 보통 2D 평면 이동)
            targetPos.y = currentPos.y;

            float distance = math.distance(currentPos, targetPos);

            // 1. 코너링 (웨이포인트 스위칭)
            // 다음 웨이포인트가 있고, 코너링 반경 안에 들어왔다면 타겟 변경
            if (waypoints.HasNext && distance < CornerRadius)
            {
                waypoints.Current = waypoints.Next;
                waypoints.HasNext = false; // 소비함

                // 타겟 변경 후 재계산
                targetPos = waypoints.Current;
                targetPos.y = currentPos.y;
                distance = math.distance(currentPos, targetPos);
            }

            // 2. 이동할 필요가 없으면 리턴 (도착 판정은 ArrivalSystem에서 함)
            if (distance <= 0.001f) return;

            // 3. 방향 계산
            float3 toTarget = targetPos - currentPos;
            float3 finalDirection = math.normalize(toTarget);

            // 4. 벽/유닛 슬라이딩 (Raycast)
            // 장애물에 비비며 이동하도록 처리
            var rayInput = new RaycastInput
            {
                Start = currentPos,
                End = currentPos + (finalDirection * WallCheckDistance),
                Filter = CollisionFilter
            };

            if (CollisionWorld.CastRay(rayInput, out var hit))
            {
                float3 normal = hit.SurfaceNormal;
                float dot = math.dot(finalDirection, normal);

                // 벽을 향해 가고 있다면 (내적 < 0)
                if (dot < 0)
                {
                    // 벽면을 따라가는 벡터 투영
                    float3 slideDir = finalDirection - (dot * normal);
                    if (math.lengthsq(slideDir) > 0.0001f)
                    {
                        finalDirection = math.normalize(slideDir);
                    }
                }
            }

            // 6. 최종 위치 적용
            float moveStep = speed.Value * DeltaTime;
            transform.Position += finalDirection * moveStep;

            // 7. 회전 (바라보는 방향) - Slerp로 부드럽게 보간
            if (math.lengthsq(finalDirection) > 0.001f)
            {
                quaternion targetRotation = quaternion.LookRotationSafe(finalDirection, math.up());
                float t = math.saturate(DeltaTime * RotationSpeed);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, t);
            }
        }
    }
}