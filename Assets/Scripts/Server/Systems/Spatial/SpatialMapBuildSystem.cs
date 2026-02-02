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
    /// <para>- Persistent 맵을 한 번 할당 후, 매 프레임 Job 기반 Clear + 재빌드로 재사용</para>
    /// <para>- CompleteDependency 없이 Job dependency chain만으로 동기화</para>
    /// </summary>
    [UpdateInGroup(typeof(SpatialPartitioningGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct SpatialMapBuildSystem : ISystem
    {
        private EntityQuery _targetingQuery;
        private EntityQuery _movementQuery;
        private Entity _spatialMapsEntity;

        private NativeParallelMultiHashMap<int, SpatialTargetEntry> _targetingMap;
        private NativeParallelMultiHashMap<int, SpatialMovementEntry> _movementMap;

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

            // Persistent 맵 할당
            _targetingMap = new NativeParallelMultiHashMap<int, SpatialTargetEntry>(256, Allocator.Persistent);
            _movementMap = new NativeParallelMultiHashMap<int, SpatialMovementEntry>(512, Allocator.Persistent);

            // SpatialMaps 싱글톤 엔티티 생성
            _spatialMapsEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(_spatialMapsEntity, new SpatialMaps
            {
                TargetingMap = _targetingMap,
                MovementMap = _movementMap,
                IsValid = false
            });
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_targetingMap.IsCreated) _targetingMap.Dispose();
            if (_movementMap.IsCreated) _movementMap.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int targetingCount = _targetingQuery.CalculateEntityCount();
            int movementCount = _movementQuery.CalculateEntityCount();

            // capacity 확보 (대형 유닛 멀티셀 등록 + 여유분)
            int requiredTargeting = math.max(64, targetingCount * 2);
            int requiredMovement = math.max(64, movementCount * 4);

            bool needResize = _targetingMap.Capacity < requiredTargeting
                           || _movementMap.Capacity < requiredMovement;
            if (needResize)
            {
                // Capacity 변경은 메인 스레드 작업 → 이전 읽기 Job 완료 필요
                state.CompleteDependency();
                if (_targetingMap.Capacity < requiredTargeting)
                    _targetingMap.Capacity = requiredTargeting;
                if (_movementMap.Capacity < requiredMovement)
                    _movementMap.Capacity = requiredMovement;
            }

            // Job 기반 Clear: state.Dependency에 이전 소비 시스템의 read handle 포함
            // ClearJob은 모든 읽기 완료 후 자동 실행 (메인 스레드 블로킹 없음)
            var clearTargetingHandle = new ClearTargetingMapJob { Map = _targetingMap }
                .Schedule(state.Dependency);
            var clearMovementHandle = new ClearMovementMapJob { Map = _movementMap }
                .Schedule(state.Dependency);

            if (targetingCount == 0 && movementCount == 0)
            {
                state.Dependency = JobHandle.CombineDependencies(clearTargetingHandle, clearMovementHandle);
                SystemAPI.SetSingleton(new SpatialMaps
                {
                    TargetingMap = _targetingMap,
                    MovementMap = _movementMap,
                    IsValid = false
                });
                return;
            }

            // Build Job: Clear 완료 후 실행 (dependency chain)
            var buildTargetingJob = new BuildTargetingMapJob
            {
                SpatialMap = _targetingMap.AsParallelWriter(),
                CellSize = SpatialHashUtility.TargetingCellSize
            };
            var buildMovementJob = new BuildMovementMapJob
            {
                SpatialMap = _movementMap.AsParallelWriter(),
                CellSize = SpatialHashUtility.MovementCellSize
            };

            var targetingHandle = buildTargetingJob.ScheduleParallel(_targetingQuery, clearTargetingHandle);
            var movementHandle = buildMovementJob.ScheduleParallel(_movementQuery, clearMovementHandle);
            state.Dependency = JobHandle.CombineDependencies(targetingHandle, movementHandle);

            SystemAPI.SetSingleton(new SpatialMaps
            {
                TargetingMap = _targetingMap,
                MovementMap = _movementMap,
                IsValid = true
            });
        }
    }

    // =========================================================================
    // Clear Jobs (Persistent 맵 재사용을 위한 Job 기반 Clear)
    // =========================================================================

    [BurstCompile]
    public struct ClearTargetingMapJob : IJob
    {
        public NativeParallelMultiHashMap<int, SpatialTargetEntry> Map;
        public void Execute() => Map.Clear();
    }

    [BurstCompile]
    public struct ClearMovementMapJob : IJob
    {
        public NativeParallelMultiHashMap<int, SpatialMovementEntry> Map;
        public void Execute() => Map.Clear();
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
