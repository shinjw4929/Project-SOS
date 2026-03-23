using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 초기 배치 벽 자동 파괴 시스템.
    /// InitialWallTag가 있는 벽에 타이머를 추가하고, 시간이 지나면 파괴.
    /// 파괴 전 Grid 점유(IsOccupied, IsPathBlocked)를 직접 해제.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InitialWallDecaySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameSettings>();
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gameSettings = SystemAPI.GetSingleton<GameSettings>();
            float deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // Phase 1: 타이머가 없는 초기 벽에 타이머 추가
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<InitialWallTag>>()
                    .WithNone<InitialWallDecayTimer>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new InitialWallDecayTimer
                {
                    RemainingTime = gameSettings.InitialWallDecayTime
                });
            }

            // Phase 2: 타이머 업데이트 및 파괴
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            var gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            int gridSizeX = gridSettings.GridSize.x;
            bool anyDecayed = false;

            foreach (var (timer, gridPos, footprint, entity) in
                SystemAPI.Query<RefRW<InitialWallDecayTimer>, RefRO<GridPosition>, RefRO<StructureFootprint>>()
                    .WithEntityAccess())
            {
                timer.ValueRW.RemainingTime -= deltaTime;

                if (timer.ValueRO.RemainingTime <= 0)
                {
                    int2 pos = gridPos.ValueRO.Position;
                    int w = footprint.ValueRO.Width;
                    int l = footprint.ValueRO.Length;

                    // Grid 점유 직접 해제 (Cleanup 시스템에 의존하지 않음)
                    GridUtility.UnmarkOccupied(gridBuffer, pos.x, pos.y, w, l, gridSizeX);
                    int pathWidth = math.max(1, w - 2);
                    int pathLength = math.max(1, l - 2);
                    GridUtility.UnmarkPathBlocked(gridBuffer,
                        pos.x, pos.y, w, l,
                        pathWidth, pathLength,
                        gridSizeX);

                    anyDecayed = true;
                    ecb.DestroyEntity(entity);
                }
            }

            // Flow Field 캐시 무효화
            if (anyDecayed && SystemAPI.TryGetSingleton<FlowFieldCacheData>(out var cache))
            {
                cache.IsGridStale = true;
                SystemAPI.SetSingleton(cache);
            }
        }
    }
}
