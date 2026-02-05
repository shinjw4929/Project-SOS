using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 건물 엔티티 생성 시 NavMeshObstacle GameObject를 동적으로 생성하는 시스템
    /// - NeedsNavMeshObstacle 태그가 있는 건물에 대해 GameObject + NavMeshObstacle 생성
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class NavMeshObstacleSpawnSystem : SystemBase
    {
        private const float PathInvalidationRadius = 8f;

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (transform, footprint, entity) in
                SystemAPI.Query<RefRO<LocalTransform>, RefRO<StructureFootprint>>()
                    .WithAny<StructureTag, ResourceNodeTag>()
                    .WithAll<NeedsNavMeshObstacle>()
                    .WithEntityAccess())
            {
                // 1. GameObject 생성
                GameObject obstacleObj = new GameObject($"NavMeshObstacle_{entity.Index}");
                obstacleObj.transform.position = transform.ValueRO.Position;
                obstacleObj.transform.rotation = transform.ValueRO.Rotation;

                // 2. NavMeshObstacle 컴포넌트 추가 및 설정
                NavMeshObstacle obstacle = obstacleObj.AddComponent<NavMeshObstacle>();

                // 3. 형태에 따른 크기 및 중심점 설정
                float obstacleHeight = footprint.ValueRO.WorldHeight;

                if (footprint.ValueRO.IsCircular)
                {
                    // 원형 장애물 (Capsule)
                    obstacle.shape = NavMeshObstacleShape.Capsule;
                    obstacle.center = new Vector3(0, obstacleHeight * 0.5f, 0);
                    obstacle.radius = math.max(0.1f, footprint.ValueRO.WorldRadius);
                    obstacle.height = obstacleHeight;
                }
                else
                {
                    // 박스형 장애물 (Box)
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.center = new Vector3(0, obstacleHeight * 0.5f, 0);

                    obstacle.size = new Vector3(
                        footprint.ValueRO.WorldWidth,
                        obstacleHeight,
                        footprint.ValueRO.WorldLength
                    );
                }

                // Carving 설정 (지연 적용으로 프레임 스파이크 방지)
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;   // 정지 상태에서만 carving
                obstacle.carvingMoveThreshold = 0.1f;
                obstacle.carvingTimeToStationary = 0.5f;  // 0.5초 지연

                // 4. Managed Component 추가 (GameObject 직접 참조)
                ecb.AddComponent(entity, new NavMeshObstacleReference
                {
                    ObstacleObject = obstacleObj
                });

                // 5. NeedsNavMeshObstacle 태그 제거 (처리 완료)
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(entity, false);

                // 6. 건물 내부 엔티티 밀어내기 + 주변 경로 재계산
                PushAndInvalidateNearbyPaths(transform.ValueRO.Position, footprint.ValueRO);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        private void PushAndInvalidateNearbyPaths(float3 buildingPos, StructureFootprint footprint)
        {
            float halfW, halfL;
            bool isCircular = footprint.IsCircular;

            if (isCircular)
            {
                halfW = halfL = footprint.WorldRadius;
            }
            else
            {
                halfW = footprint.WorldWidth * 0.5f;
                halfL = footprint.WorldLength * 0.5f;
            }

            foreach (var (goalState, entityTransform, obstacle, velocity, waypointsEnabled) in
                     SystemAPI.Query<RefRW<MovementGoal>, RefRW<LocalTransform>, RefRO<ObstacleRadius>,
                             RefRW<PhysicsVelocity>, EnabledRefRW<MovementWaypoints>>()
                         .WithAny<UnitTag, EnemyTag>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                float3 local = entityTransform.ValueRO.Position - buildingPos;
                local.y = 0;
                float entityR = obstacle.ValueRO.Radius;

                bool isInside;
                if (isCircular)
                {
                    float dist = math.length(local);
                    isInside = dist < halfW + entityR;
                }
                else
                {
                    // AABB 겹침 판정: 엔티티 원과 건물 박스의 실제 겹침 체크
                    isInside = math.abs(local.x) < halfW + entityR &&
                               math.abs(local.z) < halfL + entityR;
                }

                if (isInside)
                {
                    if (isCircular)
                    {
                        float dist = math.length(local);
                        float pushDist = halfW + entityR - dist;
                        float3 pushDir = dist > 0.01f ? local / dist : new float3(1, 0, 0);
                        entityTransform.ValueRW.Position += pushDir * (pushDist + 0.1f);
                    }
                    else
                    {
                        // 가장 가까운 변으로 밀어냄 (최소 침투 축)
                        float overlapX = (halfW + entityR) - math.abs(local.x);
                        float overlapZ = (halfL + entityR) - math.abs(local.z);

                        if (overlapX < overlapZ)
                        {
                            float sign = local.x >= 0 ? 1f : -1f;
                            entityTransform.ValueRW.Position.x += sign * (overlapX + 0.1f);
                        }
                        else
                        {
                            float sign = local.z >= 0 ? 1f : -1f;
                            entityTransform.ValueRW.Position.z += sign * (overlapZ + 0.1f);
                        }
                    }

                    // 이동 즉시 중지
                    waypointsEnabled.ValueRW = false;
                    velocity.ValueRW.Linear = float3.zero;
                }
                else if (math.lengthsq(local) < PathInvalidationRadius * PathInvalidationRadius
                         && waypointsEnabled.ValueRO)
                {
                    goalState.ValueRW.IsPathDirty = true;
                }
            }
        }
    }
}