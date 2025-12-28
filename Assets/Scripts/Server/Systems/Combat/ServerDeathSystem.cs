using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Shared;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[BurstCompile] // 시스템 전체에 적용
public partial struct ServerDeathSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 멀티쓰레드에서 안전한 병렬 Writer ECB 생성
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        // 잡(Job) 예약 -> 워커 스레드들이 병렬로 처리
        new ServerDeathJob
        {
            Ecb = ecb
        }.ScheduleParallel(); 
    }
}

// [IJobEntity] 자동으로 쿼리를 생성하고 병렬 처리해주는 잡
[BurstCompile]
public partial struct ServerDeathJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter Ecb;

    // [WithChangeFilter] Health 컴포넌트가 '수정된' 엔티티만 이 함수를 실행함 (성능 핵심)
    private void Execute([EntityIndexInQuery] int sortKey, Entity entity, ref Health health)
    {
        // 체력이 변한 애들 중, 0 이하인 경우만
        if (health.CurrentValue <= 0)
        {
            // 병렬 쓰기 시 sortKey 필수
            Ecb.DestroyEntity(sortKey, entity);
        }
    }
}