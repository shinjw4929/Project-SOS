using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using System.Runtime.CompilerServices;
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct ProductionProgressSystem : ISystem
    {
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        private EntityQuery _unitQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<UnitCatalog>();

            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _unitQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<UnitTag, LocalTransform>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);
            float deltaTime = SystemAPI.Time.DeltaTime;

            var catalogEntity = SystemAPI.GetSingletonEntity<UnitCatalog>();
            var catalogBuffer = SystemAPI.GetBuffer<UnitCatalogElement>(catalogEntity);

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();

            float spawnOffset = SystemAPI.TryGetSingleton<GameSettings>(out var gs)
                ? gs.UnitSpawnOffset : 1.0f;
            float spawnClearanceRatio = gs.SpawnClearanceRatio > 0f
                ? gs.SpawnClearanceRatio : 0.8f;

            // 생산 완료 임박 건물이 있는지 사전 확인 (O(생산건물 수), 보통 1~3개)
            bool anyCompletingThisFrame = false;
            foreach (var queue in SystemAPI.Query<RefRO<ProductionQueue>>()
                .WithAll<ProductionFacilityTag>())
            {
                if (queue.ValueRO.IsActive &&
                    queue.ValueRO.Progress + deltaTime >= queue.ValueRO.Duration)
                {
                    anyCompletingThisFrame = true;
                    break;
                }
            }

            // 생산 완료 시에만 유닛 위치 수집 (sync point + 할당 회피)
            NativeArray<float3> unitPositions;
            if (anyCompletingThisFrame)
            {
                var unitTransforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
                unitPositions = new NativeArray<float3>(unitTransforms.Length, Allocator.TempJob);
                for (int i = 0; i < unitTransforms.Length; i++)
                    unitPositions[i] = unitTransforms[i].Position;
                unitTransforms.Dispose();
            }
            else
            {
                unitPositions = new NativeArray<float3>(0, Allocator.TempJob);
            }

            new ProductionUpdateJob
            {
                DeltaTime = deltaTime,
                CellSize = gridSettings.CellSize,
                UnitSpawnOffset = spawnOffset,
                CatalogBuffer = catalogBuffer.AsNativeArray(),
                Ecb = ecb,
                TransformLookup = _transformLookup,
                UnitPositions = unitPositions,
                SpawnClearanceRatio = spawnClearanceRatio
            }.ScheduleParallel();

            state.Dependency = unitPositions.Dispose(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(ProductionFacilityTag))]
    public partial struct ProductionUpdateJob : IJobEntity
    {
        public float DeltaTime;
        public float CellSize;
        public float UnitSpawnOffset;
        [ReadOnly] public NativeArray<UnitCatalogElement> CatalogBuffer;
        public EntityCommandBuffer.ParallelWriter Ecb;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public NativeArray<float3> UnitPositions;
        public float SpawnClearanceRatio;

        private void Execute(
            [EntityIndexInQuery] int sortKey,
            Entity entity,
            ref ProductionQueue queue,
            in LocalTransform transform,
            in GhostOwner owner,
            in StructureFootprint footprint)
        {
            if (!queue.IsActive) return;

            queue.Progress += DeltaTime;

            if (queue.Progress >= queue.Duration)
            {
                int unitIndex = queue.ProducingUnitIndex;

                if (unitIndex >= 0 && unitIndex < CatalogBuffer.Length)
                {
                    Entity prefab = CatalogBuffer[unitIndex].PrefabEntity;

                    float halfWorldWidth = footprint.Width * CellSize * 0.5f;
                    float halfWorldLength = footprint.Length * CellSize * 0.5f;

                    float3 spawnPos = FindFreeSpawnPosition(
                        transform.Position, halfWorldWidth, halfWorldLength);

                    SpawnUnit(sortKey, prefab, spawnPos, owner.NetworkId);
                }

                queue = new ProductionQueue
                {
                    ProducingUnitIndex = -1,
                    Progress = 0,
                    Duration = 0,
                    IsActive = false
                };
            }
        }

        /// <summary>
        /// 4시~8시 방향 아크에서 4시부터 순서대로 빈 자리를 탐색.
        /// 아크: 우측 하반부(4시) → 하단(6시) → 좌측 하반부(8시).
        /// </summary>
        private float3 FindFreeSpawnPosition(float3 center, float hw, float hl)
        {
            float segRight = hl * 0.5f;
            float segBottom = 2f * hw;
            float segLeft = hl * 0.5f;
            float arcLength = segRight + segBottom + segLeft;

            if (arcLength < 0.01f)
                return center + new float3(UnitSpawnOffset, 0f, -UnitSpawnOffset);

            int candidateCount = math.max(4, (int)math.ceil(arcLength / UnitSpawnOffset));
            float step = arcLength / candidateCount;
            float clearance = UnitSpawnOffset * SpawnClearanceRatio;
            float clearanceSq = clearance * clearance;

            float bestDistSq = -1f;
            float3 bestPos = GetArcPosition(center, hw, hl, 0f, segRight, segBottom);

            for (int c = 0; c < candidateCount; c++)
            {
                float t = c * step;
                float3 candidate = GetArcPosition(center, hw, hl, t, segRight, segBottom);

                float minDistSq = float.MaxValue;
                for (int u = 0; u < UnitPositions.Length; u++)
                {
                    float dx = candidate.x - UnitPositions[u].x;
                    float dz = candidate.z - UnitPositions[u].z;
                    float dSq = dx * dx + dz * dz;
                    if (dSq < minDistSq) minDistSq = dSq;
                }

                if (minDistSq >= clearanceSq)
                    return candidate;

                if (minDistSq > bestDistSq)
                {
                    bestDistSq = minDistSq;
                    bestPos = candidate;
                }
            }

            return bestPos;
        }

        /// <summary>
        /// 4시~8시 아크의 t 위치를 월드 좌표로 변환.
        /// 4시(우측 하반부) → 6시(하단) → 8시(좌측 하반부).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 GetArcPosition(float3 center, float hw, float hl,
            float t, float segRight, float segBottom)
        {
            float off = UnitSpawnOffset;

            if (t < segRight)
            {
                return new float3(
                    center.x + hw + off, 0f,
                    center.z - hl * 0.5f - t);
            }

            float t2 = t - segRight;
            if (t2 < segBottom)
            {
                return new float3(
                    center.x + hw - t2, 0f,
                    center.z - hl - off);
            }

            float t3 = t2 - segBottom;
            return new float3(
                center.x - hw - off, 0f,
                center.z - hl + t3);
        }

        private void SpawnUnit(int sortKey, Entity prefab, float3 spawnPos, int ownerId)
        {
            if (prefab == Entity.Null) return;

            Entity newUnit = Ecb.Instantiate(sortKey, prefab);

            if (TransformLookup.HasComponent(prefab))
            {
                LocalTransform prefabTransform = TransformLookup[prefab];
                prefabTransform.Position += spawnPos;
                Ecb.SetComponent(sortKey, newUnit, prefabTransform);
            }
            else
            {
                Ecb.SetComponent(sortKey, newUnit, LocalTransform.FromPosition(spawnPos));
            }

            Ecb.SetComponent(sortKey, newUnit, new GhostOwner { NetworkId = ownerId });
            Ecb.SetComponent(sortKey, newUnit, new Team { teamId = ownerId });
        }
    }
}
