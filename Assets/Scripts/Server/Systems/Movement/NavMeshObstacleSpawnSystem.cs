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
    /// - ObstaclePadding을 적용하여 NavMesh 구멍을 실제 건물보다 살짝 작게 뚫음 (유닛이 딱 붙게 하기 위함)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    // ObstacleGridInitSystem은 InitializationSystemGroup에 있으므로 UpdateAfter 불필요
    // SystemGroup 실행 순서: Initialization → Simulation
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class NavMeshObstacleSpawnSystem : SystemBase
    {
        private const float PathInvalidationRadius = 15f;
        
        // [핵심 설정] 장애물을 실제 크기보다 얼마나 작게 만들 것인가?
        // NavMesh Agent Radius(보통 0.5)만큼 NavMesh가 자동으로 벌어지므로,
        // 이를 상쇄하기 위해 장애물 자체를 작게 만듭니다.
        // 값 추천: 0.2f ~ 0.5f (너무 크면 유닛이 건물 안으로 파고듬)
        private const float ObstaclePadding = 0.5f;

        protected override void OnCreate()
        {
            RequireForUpdate<GridSettings>();
        }

        protected override void OnUpdate()
        {
            // GridSettings 가져오기 (CellSize 계산용)
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            float cellSize = gridSettings.CellSize;

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
                obstacle.shape = NavMeshObstacleShape.Box;

                // 3. 크기 및 중심점 계산
                
                // 중심점: 높이의 절반만큼 올려야 바닥에 묻히지 않음
                obstacle.center = new Vector3(0, footprint.ValueRO.Height * 0.5f, 0);

                // 월드 크기 계산: 그리드 칸 수 * 셀 크기
                float worldWidth = footprint.ValueRO.Width * cellSize;
                float worldLength = footprint.ValueRO.Length * cellSize;

                // [핵심] Padding 적용: 실제 크기에서 Padding만큼 빼서 장애물을 축소
                // math.max(0.1f, ...)는 크기가 음수가 되는 것을 방지
                float sizeX = math.max(0.1f, worldWidth - ObstaclePadding);
                float sizeZ = math.max(0.1f, worldLength - ObstaclePadding);

                obstacle.size = new Vector3(
                    sizeX,
                    footprint.ValueRO.Height,
                    sizeZ
                );

                // Carving 설정
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false; // 즉시 적용을 위해 false
                obstacle.carvingMoveThreshold = 0.1f;
                obstacle.carvingTimeToStationary = 0f;

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