using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Rendering; // [필수] DisableRendering이 여기 있음
using Shared;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[BurstCompile]
public partial struct ClientDeathSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        new ClientDeathJob
        {
            Ecb = ecb
        }.ScheduleParallel();
    }
}

// [IJobEntity] 최적화된 잡
[BurstCompile]
// ▼ [중요] DisableRendering이 "없는" 애들만 찾으라고 명시
[WithNone(typeof(DisableRendering))] 
public partial struct ClientDeathJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter Ecb;

    // 매개변수에서 DisableRendering을 뺍니다. (어트리뷰트로 처리했으므로)
    private void Execute([EntityIndexInQuery] int sortKey, Entity entity, ref Health health)
    {
        // 체력이 0 이하인 경우
        if (health.CurrentValue <= 0)
        {
            // 삭제 대신 렌더링 끄기 (서버가 지울 때까지 숨김)
            Ecb.AddComponent<DisableRendering>(sortKey, entity);
        }
    }
}