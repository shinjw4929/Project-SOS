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
    /// - 생성된 GameObject의 InstanceID를 NavMeshObstacleProxy에 저장
    /// - NavMesh Carving을 활성화하여 경로 계산 시 장애물로 인식되도록 함
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class NavMeshObstacleSpawnSystem : SystemBase
    {
        private const float PathInvalidationRadius = 15f;

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (transform, footprint, entity) in
                SystemAPI.Query<RefRO<LocalTransform>, RefRO<StructureFootprint>>()
                    .WithAll<StructureTag, NeedsNavMeshObstacle>()
                    .WithEntityAccess())
            {
                // GameObject 생성
                GameObject obstacleObj = new GameObject($"NavMeshObstacle_{entity.Index}");
                obstacleObj.transform.position = transform.ValueRO.Position;
                obstacleObj.transform.rotation = transform.ValueRO.Rotation;

                // NavMeshObstacle 컴포넌트 추가 및 설정
                NavMeshObstacle obstacle = obstacleObj.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;

                // 건물 크기에 맞춰 Obstacle 설정
                obstacle.center = new Vector3(0, footprint.ValueRO.Height * 0.5f, 0);
                obstacle.size = new Vector3(
                    footprint.ValueRO.Width,
                    footprint.ValueRO.Height,
                    footprint.ValueRO.Length
                );

                // Carving 활성화 (NavMesh에서 구멍 뚫기)
                obstacle.carving = true;

                // 건물은 정적 오브젝트이므로 즉시 Carving 시작
                obstacle.carveOnlyStationary = false;

                // 이동하는 장애물이 아니므로 threshold 설정 불필요
                obstacle.carvingMoveThreshold = 0.1f;
                obstacle.carvingTimeToStationary = 0f;  // 즉시 Carving

                // Managed Component 추가 (GameObject 직접 참조)
                ecb.AddComponent(entity, new NavMeshObstacleReference
                {
                    ObstacleObject = obstacleObj
                });

                // NeedsNavMeshObstacle 태그 제거 (처리 완료)
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(entity, false);

                // 주변 유닛의 경로 무효화 (NavMesh 업데이트 반영)
                InvalidateNearbyPaths(transform.ValueRO.Position);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// 건물 주변 유닛의 경로를 무효화하여 NavMesh 업데이트 반영
        /// </summary>
        private void InvalidateNearbyPaths(float3 buildingPos)
        {
            foreach (var (pathState, unitTransform, moveTarget) in
                SystemAPI.Query<RefRW<PathfindingState>, RefRO<LocalTransform>, RefRO<MoveTarget>>()
                    .WithAll<UnitTag>())
            {
                // 이동 중인 유닛만 확인
                if (!moveTarget.ValueRO.isValid)
                    continue;

                float distance = Unity.Mathematics.math.distance(unitTransform.ValueRO.Position, buildingPos);

                // 주변 반경 내에 있으면 경로 재계산 요청
                if (distance < PathInvalidationRadius)
                {
                    pathState.ValueRW.NeedsPath = true;
                }
            }
        }
    }
}
