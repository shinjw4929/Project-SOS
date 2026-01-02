using Unity.Burst;
using Unity.Entities;


namespace Shared
{
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    partial struct SharedBootstrapSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }
        
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
            
            var entityManager = state.EntityManager;
            
            // 1. UserState 생성
            if (!SystemAPI.HasSingleton<UserResources>())
            {
                var entity = entityManager.CreateEntity(typeof(UserResources));
                
                // 데이터 값 설정
                SystemAPI.SetComponent(entity, new UserResources
                {
                    Resources = 100,
                    CurrentPopulation = 0,
                    MaxPopulation = 300,
                });

#if UNITY_EDITOR
                entityManager.SetName(entity, "Singleton_UserResources");
#endif
            }
        }
        
    }
}
