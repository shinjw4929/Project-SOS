using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// Wave별 적 스폰 시스템.
    /// Wave0: 초기 30마리 (EnemyBig)
    /// Wave1: 주기적 스폰 (EnemySmall + EnemyBig)
    /// Wave2: 주기적 스폰 (EnemySmall + EnemyBig + EnemyFlying)
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WaveManagerSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct EnemySpawnerSystem : ISystem
    {
        private EntityQuery _spawnPointQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GamePhaseState>();
            state.RequireForUpdate<GameSettings>();
            state.RequireForUpdate<EnemyPrefabCatalog>();
            state.RequireForUpdate<EnemySpawnPoint>();

            _spawnPointQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EnemySpawnPoint>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gameSettings = SystemAPI.GetSingleton<GameSettings>();

            if (!SystemAPI.TryGetSingletonEntity<GamePhaseState>(out Entity phaseStateEntity))
                return;

            var phaseState = SystemAPI.GetSingleton<GamePhaseState>();
            var catalog = SystemAPI.GetSingleton<EnemyPrefabCatalog>();

            // 스폰 포인트 배열
            var spawnPoints = _spawnPointQuery.ToComponentDataArray<EnemySpawnPoint>(Allocator.Temp);
            if (spawnPoints.Length == 0)
            {
                spawnPoints.Dispose();
                return;
            }

            float deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 명시적 필드로 프리팹 참조
            Entity prefabSmall = catalog.SmallPrefab;
            Entity prefabBig = catalog.BigPrefab;
            Entity prefabFlying = catalog.FlyingPrefab;

            bool stateChanged = false;

            switch (phaseState.CurrentWave)
            {
                case WavePhase.Wave0:
                    stateChanged = HandleWave0Spawn(
                        ref state, ref ecb, ref phaseState,
                        prefabBig, spawnPoints, gameSettings);
                    break;

                case WavePhase.Wave1:
                    stateChanged = HandlePeriodicSpawn(
                        ref state, ref ecb, ref phaseState, deltaTime,
                        gameSettings.Wave1SpawnInterval, gameSettings.Wave1SpawnCount,
                        spawnPoints, prefabSmall, prefabBig, Entity.Null);
                    break;

                case WavePhase.Wave2:
                    stateChanged = HandlePeriodicSpawn(
                        ref state, ref ecb, ref phaseState, deltaTime,
                        gameSettings.Wave2SpawnInterval, gameSettings.Wave2SpawnCount,
                        spawnPoints, prefabSmall, prefabBig, prefabFlying);
                    break;
            }

            if (stateChanged)
            {
                ecb.SetComponent(phaseStateEntity, phaseState);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            spawnPoints.Dispose();
        }

        private bool HandleWave0Spawn(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            ref GamePhaseState phaseState,
            Entity prefabBig,
            NativeArray<EnemySpawnPoint> spawnPoints,
            GameSettings settings)
        {
            // 이미 초기 스폰 완료
            if (phaseState.Wave0SpawnedCount >= settings.Wave0InitialSpawnCount)
                return false;

            int toSpawn = settings.Wave0InitialSpawnCount - phaseState.Wave0SpawnedCount;

            // 랜덤 시드 초기화
            var random = Random.CreateFromIndex((uint)(SystemAPI.Time.ElapsedTime * 1000 + 1));

            for (int i = 0; i < toSpawn; i++)
            {
                // 랜덤 스폰 포인트 선택
                int spawnIndex = random.NextInt(0, spawnPoints.Length);
                float3 basePos = spawnPoints[spawnIndex].Position;

                // 스폰 위치에 약간의 랜덤 오프셋 추가
                float3 offset = new float3(
                    random.NextFloat(-3f, 3f),
                    0,
                    random.NextFloat(-3f, 3f)
                );
                float3 spawnPos = basePos + offset;

                Entity enemy = ecb.Instantiate(prefabBig);
                ecb.SetComponent(enemy, LocalTransform.FromPosition(spawnPos));
            }

            phaseState.Wave0SpawnedCount = settings.Wave0InitialSpawnCount;
            return true;
        }

        private bool HandlePeriodicSpawn(
            ref SystemState state,
            ref EntityCommandBuffer ecb,
            ref GamePhaseState phaseState,
            float deltaTime,
            float spawnInterval,
            int spawnCount,
            NativeArray<EnemySpawnPoint> spawnPoints,
            Entity prefabSmall,
            Entity prefabBig,
            Entity prefabFlying)
        {
            phaseState.SpawnTimer += deltaTime;

            if (phaseState.SpawnTimer < spawnInterval)
                return true; // 타이머만 업데이트됨

            phaseState.SpawnTimer -= spawnInterval;

            var random = Random.CreateFromIndex((uint)(SystemAPI.Time.ElapsedTime * 1000 + 1));

            for (int i = 0; i < spawnCount; i++)
            {
                // 랜덤 스폰 포인트 선택
                int spawnIndex = random.NextInt(0, spawnPoints.Length);
                float3 basePos = spawnPoints[spawnIndex].Position;

                float3 offset = new float3(
                    random.NextFloat(-2f, 2f),
                    0,
                    random.NextFloat(-2f, 2f)
                );
                float3 spawnPos = basePos + offset;

                // 적 타입 랜덤 선택
                Entity prefab = SelectEnemyPrefab(ref random, prefabSmall, prefabBig, prefabFlying);
                if (prefab == Entity.Null) continue;

                // 비행 적은 공중에 스폰
                float3 finalSpawnPos = spawnPos;
                if (prefab == prefabFlying)
                {
                    finalSpawnPos.y += 5f;
                }

                Entity enemy = ecb.Instantiate(prefab);
                ecb.SetComponent(enemy, LocalTransform.FromPosition(finalSpawnPos));
            }

            return true;
        }

        private Entity SelectEnemyPrefab(
            ref Random random,
            Entity prefabSmall,
            Entity prefabBig,
            Entity prefabFlying)
        {
            // 확률 분배: Small 50%, Big 35%, Flying 15% (Flying 없으면 Small/Big만)
            float roll = random.NextFloat(0f, 1f);

            if (prefabFlying != Entity.Null)
            {
                if (roll < 0.50f) return prefabSmall;
                if (roll < 0.85f) return prefabBig;
                return prefabFlying;
            }
            else
            {
                if (roll < 0.60f) return prefabSmall;
                return prefabBig;
            }
        }
    }
}
