using Unity.Entities;
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

                // 6. 주변 유닛 경로 재계산 요청
                InvalidateNearbyPaths(transform.ValueRO.Position);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        private void InvalidateNearbyPaths(float3 buildingPos)
        {
            // 이동 중인 유닛(EnabledRefRW<MovementWaypoints>가 참인)을 찾아서 경로 갱신 요청
            foreach (var (goalState, unitTransform, waypointsEnabled) in
                     SystemAPI.Query<RefRW<MovementGoal>, RefRO<LocalTransform>, EnabledRefRW<MovementWaypoints>>()
                         .WithAll<UnitTag>())
            {
                // 이동 중이 아니면 스킵
                if (!waypointsEnabled.ValueRO)
                    continue;

                float distance = math.distance(unitTransform.ValueRO.Position, buildingPos);

                if (distance < PathInvalidationRadius)
                {
                    // 경로가 더러워졌으니(장애물 생김) 다시 계산해라
                    goalState.ValueRW.IsPathDirty = true;
                }
            }
        }
    }
}