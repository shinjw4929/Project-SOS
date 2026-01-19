using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 공간 분할 맵 빌드 시스템
    /// <para>- TargetingMap (10.0f): 유닛+건물+적 (WallTag 제외)</para>
    /// <para>- MovementMap (3.0f): 유닛+적 (대형 유닛 AABB 등록)</para>
    /// <para>- 매 프레임 SpatialMaps 싱글톤에 맵 참조 저장</para>
    /// </summary>
    [UpdateInGroup(typeof(SpatialPartitioningGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct SpatialMapBuildSystem : ISystem
    {
        private EntityQuery _targetingQuery;
        private EntityQuery _movementQuery;
        private Entity _spatialMapsEntity;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();

            // TargetingMap 쿼리: (LocalTransform + Team + Health) AND (UnitTag OR StructureTag OR EnemyTag) - WallTag
            var targetingDesc = new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<Team>(),
                    ComponentType.ReadOnly<Health>()
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<StructureTag>(),
                    ComponentType.ReadOnly<EnemyTag>()
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<WallTag>()
                }
            };
            _targetingQuery = state.EntityManager.CreateEntityQuery(targetingDesc);

            // MovementMap 쿼리: (LocalTransform + ObstacleRadius) AND (UnitTag OR EnemyTag)
            _movementQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, ObstacleRadius>()
                .WithAny<UnitTag, EnemyTag>()
                .Build(ref state);

            // SpatialMaps 싱글톤 엔티티 생성
            _spatialMapsEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(_spatialMapsEntity, new SpatialMaps
            {
                IsValid = false
            });
        }

        public void OnDestroy(ref SystemState state)
        {
            // 월드 종료 시 맵 정리
            if (SystemAPI.TryGetSingleton<SpatialMaps>(out var maps) && maps.IsValid)
            {
                if (maps.TargetingMap.IsCreated) maps.TargetingMap.Dispose();
                if (maps.MovementMap.IsCreated) maps.MovementMap.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 엔티티 수 계산
            int targetingCount = _targetingQuery.CalculateEntityCount();
            int movementCount = _movementQuery.CalculateEntityCount();

            if (targetingCount == 0 && movementCount == 0)
            {
                // 엔티티가 없으면 빈 맵 생성
                SystemAPI.SetSingleton(new SpatialMaps { IsValid = false });
                return;
            }

            // 해시 충돌 방지를 위한 여유 계수 적용
            int targetingCapacity = (int)(targetingCount * SpatialHashUtility.CapacityMultiplier);
            int movementCapacity = (int)(movementCount * SpatialHashUtility.CapacityMultiplier);

            // 맵 생성
            var targetingMap = new NativeParallelMultiHashMap<int, SpatialTargetEntry>(
                math.max(targetingCapacity, 16), Allocator.TempJob);
            var movementMap = new NativeParallelMultiHashMap<int, SpatialMovementEntry>(
                math.max(movementCapacity, 16), Allocator.TempJob);

            // 타겟팅 맵 빌드 Job
            var buildTargetingJob = new BuildTargetingMapJob
            {
                SpatialMap = targetingMap.AsParallelWriter(),
                CellSize = SpatialHashUtility.TargetingCellSize
            };

            // 이동 맵 빌드 Job
            var buildMovementJob = new BuildMovementMapJob
            {
                SpatialMap = movementMap.AsParallelWriter(),
                CellSize = SpatialHashUtility.MovementCellSize
            };

            // 두 Job 병렬 실행
            var targetingHandle = buildTargetingJob.ScheduleParallel(_targetingQuery, state.Dependency);
            var movementHandle = buildMovementJob.ScheduleParallel(_movementQuery, state.Dependency);

            // 두 핸들 결합
            state.Dependency = JobHandle.CombineDependencies(targetingHandle, movementHandle);

            // 싱글톤에 맵 참조 저장
            SystemAPI.SetSingleton(new SpatialMaps
            {
                TargetingMap = targetingMap,
                MovementMap = movementMap,
                IsValid = true
            });
        }
    }

    // =========================================================================
    // 타겟팅 맵 빌드 Job
    // =========================================================================

    [BurstCompile]
    [WithAny(typeof(UnitTag), typeof(StructureTag), typeof(EnemyTag))]
    [WithNone(typeof(WallTag))]
    public partial struct BuildTargetingMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, SpatialTargetEntry>.ParallelWriter SpatialMap;
        public float CellSize;

        public void Execute(Entity entity, in LocalTransform transform, in Team team, in Health health)
        {
            // 죽은 엔티티 필터링
            if (health.CurrentValue <= 0) return;

            int hash = SpatialHashUtility.GetCellHash(transform.Position, CellSize);
            SpatialMap.Add(hash, new SpatialTargetEntry
            {
                Entity = entity,
                Position = transform.Position,
                TeamId = team.teamId
            });
        }
    }

    // =========================================================================
    // 이동 맵 빌드 Job (대형 유닛 AABB 등록 지원)
    // =========================================================================

    [BurstCompile]
    [WithAny(typeof(UnitTag), typeof(EnemyTag))]
    public partial struct BuildMovementMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, SpatialMovementEntry>.ParallelWriter SpatialMap;
        public float CellSize;

        public void Execute(Entity entity, in LocalTransform transform, in ObstacleRadius obstacleRadius)
        {
            float3 pos = transform.Position;
            float radius = obstacleRadius.Radius;

            // 대형 유닛 여부 확인 (radius > CellSize * 0.5f)
            if (SpatialHashUtility.IsLargeEntity(radius, CellSize))
            {
                // AABB 계산하여 겹치는 모든 셀에 등록
                SpatialHashUtility.GetCellRange(pos, radius, CellSize, out int2 minCell, out int2 maxCell);

                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    for (int z = minCell.y; z <= maxCell.y; z++)
                    {
                        int hash = SpatialHashUtility.GetHashFromCoords(new int2(x, z));
                        SpatialMap.Add(hash, new SpatialMovementEntry { Entity = entity });
                    }
                }
            }
            else
            {
                // 일반 유닛: 단일 셀에 등록
                int hash = SpatialHashUtility.GetCellHash(pos, CellSize);
                SpatialMap.Add(hash, new SpatialMovementEntry { Entity = entity });
            }
        }
    }
}
