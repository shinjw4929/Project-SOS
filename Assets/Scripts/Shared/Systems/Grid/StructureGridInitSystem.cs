using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Shared; 

namespace Shared
{
    /// <summary>
    /// 초기 배치된 건물(Baked)의 GridPosition을 계산하고, 즉시 그리드에 점유를 등록하는 시스템
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(GridOccupancyEventSystem))] 
    [BurstCompile]
    public partial struct StructureGridInitSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            // [수정 1] ECB 시스템 싱글톤 요구
            state.RequireForUpdate<EndInitializationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 필수 싱글톤 및 버퍼 확인
            if (!SystemAPI.TryGetSingletonEntity<GridSettings>(out var gridEntity)) return;
            var gridSettings = SystemAPI.GetComponent<GridSettings>(gridEntity);
            
            if (!SystemAPI.HasBuffer<GridCell>(gridEntity)) return;
            var gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);

            // [수정 2] 시스템 ECB 생성 (EndInitialization 시점에 처리됨)
            var ecbSingleton = SystemAPI.GetSingleton<EndInitializationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (transform, gridPos, footprint, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRW<GridPosition>, RefRO<StructureFootprint>>()
                         .WithAll<StructureTag>()
                         .WithNone<GridOccupancyCleanup>()
                         .WithEntityAccess())
            {
                // 1. 좌표 계산
                int2 calculatedGridPos = GridUtility.WorldToGridForBuilding(
                    transform.ValueRO.Position,
                    footprint.ValueRO.Width,
                    footprint.ValueRO.Length,
                    gridSettings
                );

                // 2. 컴포넌트 직접 수정 (ECB 불필요, 즉시 반영)
                gridPos.ValueRW.Position = calculatedGridPos;

                // 3. 그리드 버퍼 직접 수정 (ECB 불필요, 즉시 반영)
                //    데이터 값 변경은 ECB를 거치지 않고 직접 하는 것이 성능상 유리합니다.
                GridUtility.MarkOccupied(
                    gridBuffer, 
                    calculatedGridPos, 
                    footprint.ValueRO.Width, 
                    footprint.ValueRO.Length, 
                    gridSettings.GridSize.x
                );

                // 4. NavMeshObstacle 활성화
                if (SystemAPI.HasComponent<NeedsNavMeshObstacle>(entity))
                {
                    ecb.SetComponentEnabled<NeedsNavMeshObstacle>(entity, true);
                }
                
                // 5. Cleanup 태그 추가
                ecb.AddComponent(entity, new GridOccupancyCleanup
                {
                    GridPosition = calculatedGridPos,
                    Width = footprint.ValueRO.Width,
                    Length = footprint.ValueRO.Length
                });
            }
        }
    }
}