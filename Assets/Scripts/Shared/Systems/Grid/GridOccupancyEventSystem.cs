using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 건물 생성/파괴 시 그리드 점유 상태(GridCell.isOccupied)를 갱신하는 시스템
    /// - 방식: Reactive (Cleanup 컴포넌트 활용)
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct GridOccupancyEventSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            
            // GridCell 버퍼가 없으면 리턴
            if (!SystemAPI.HasBuffer<GridCell>(gridEntity)) return;

            var gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecbAdd = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var ecbRemove = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // -----------------------------------------------------------------
            // 1. [건설 감지] 
            // 조건: GridPosition(위치)과 Footprint(크기)가 있는데, Cleanup(백업)이 없는 경우
            // 동작: 그리드 채우기 + Cleanup 컴포넌트 부착
            // -----------------------------------------------------------------
            var addJob = new AddOccupancyJob
            {
                GridBuffer = gridBuffer,
                GridSizeX = gridSettings.GridSize.x, // int2 사용
                ECB = ecbAdd.AsParallelWriter()
            };
            state.Dependency = addJob.ScheduleParallel(state.Dependency);

            // -----------------------------------------------------------------
            // 2. [파괴 감지]
            // 조건: Cleanup(백업)은 있는데, GridPosition(원본)이 사라진 경우
            // 동작: 그리드 비우기 + Cleanup 컴포넌트 제거 (완전 소멸)
            // -----------------------------------------------------------------
            var removeJob = new RemoveOccupancyJob
            {
                GridBuffer = gridBuffer,
                GridSizeX = gridSettings.GridSize.x,
                ECB = ecbRemove.AsParallelWriter()
            };
            state.Dependency = removeJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(StructureTag))] // 건물이면서
    [WithNone(typeof(GridOccupancyCleanup))] // 아직 처리 안 된 녀석
    public partial struct AddOccupancyJob : IJobEntity
    {
        [NativeDisableParallelForRestriction]
        public DynamicBuffer<GridCell> GridBuffer;
        public int GridSizeX;
        
        public EntityCommandBuffer.ParallelWriter ECB;

        // [핵심] 프리팹 조회가 아니라, 엔티티의 컴포넌트를 직접 읽음
        public void Execute(Entity entity, [ChunkIndexInQuery] int sortKey, 
            RefRO<GridPosition> pos, RefRO<StructureFootprint> footprint)
        {
            int2 position = pos.ValueRO.Position;
            int width = footprint.ValueRO.Width;
            int length = footprint.ValueRO.Length;

            // 1. 그리드 점유 (Mark)
            for (int dy = 0; dy < length; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int index = (position.y + dy) * GridSizeX + (position.x + dx);
                    if (index >= 0 && index < GridBuffer.Length)
                    {
                        var cell = GridBuffer[index];
                        cell.IsOccupied = true;
                        GridBuffer[index] = cell;
                    }
                }
            }

            // 2. Cleanup 백업 데이터 추가
            ECB.AddComponent(sortKey, entity, new GridOccupancyCleanup
            {
                GridPosition = position,
                Width = width,
                Length = length
            });
        }
    }

    [BurstCompile]
    [WithNone(typeof(GridPosition))] // 원본 위치 정보가 사라진(파괴된) 엔티티
    public partial struct RemoveOccupancyJob : IJobEntity
    {
        [NativeDisableParallelForRestriction]
        public DynamicBuffer<GridCell> GridBuffer;
        public int GridSizeX;
        
        public EntityCommandBuffer.ParallelWriter ECB;

        public void Execute(Entity entity, [ChunkIndexInQuery] int sortKey, RefRO<GridOccupancyCleanup> cleanup)
        {
            int2 gridPosition = cleanup.ValueRO.GridPosition;
            int width = cleanup.ValueRO.Width;
            int length = cleanup.ValueRO.Length;

            // 1. 그리드 해제 (Unmark)
            for (int dy = 0; dy < length; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int index = (gridPosition.y + dy) * GridSizeX + (gridPosition.x + dx);
                    if (index >= 0 && index < GridBuffer.Length)
                    {
                        var cell = GridBuffer[index];
                        cell.IsOccupied = false;
                        GridBuffer[index] = cell;
                    }
                }
            }

            // 2. Cleanup 제거 (엔티티 완전 삭제)
            ECB.RemoveComponent<GridOccupancyCleanup>(sortKey, entity);
        }
    }
}