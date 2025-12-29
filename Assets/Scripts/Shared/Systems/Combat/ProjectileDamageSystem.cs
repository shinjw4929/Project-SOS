using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
public partial struct ProjectileDamageSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var sim = SystemAPI.GetSingleton<SimulationSingleton>();

        var healthLookup = SystemAPI.GetComponentLookup<EnemyHealthData>(false);
        var projectileLookup = SystemAPI.GetComponentLookup<Projectile>(true);

        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        var job = new DamageJob
        {
            HealthLookup = healthLookup,
            ProjectileLookup = projectileLookup,
            ECB = ecb
        };

        state.Dependency = job.Schedule(sim, state.Dependency);
        state.Dependency.Complete();

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    [BurstCompile]
    private struct DamageJob : ICollisionEventsJob
    {
        public ComponentLookup<EnemyHealthData> HealthLookup;
        [ReadOnly] public ComponentLookup<Projectile> ProjectileLookup;

        public EntityCommandBuffer ECB;

        public void Execute(CollisionEvent collisionEvent)
        {
            Entity a = collisionEvent.EntityA;
            Entity b = collisionEvent.EntityB;

            bool aProj = ProjectileLookup.HasComponent(a);
            bool bProj = ProjectileLookup.HasComponent(b);

            if (!aProj && !bProj) return;

            Entity proj = aProj ? a : b;
            Entity other = aProj ? b : a;

            if (!HealthLookup.HasComponent(other)) return;

            var hp = HealthLookup[other];
            if (hp.Current <= 0)
            {
                ECB.DestroyEntity(proj);
                return;
            }

            hp.Current -= 1;
            if (hp.Current < 0) hp.Current = 0;

            HealthLookup[other] = hp;

            ECB.DestroyEntity(proj);

            if (hp.Current == 0)
            {
                ECB.DestroyEntity(other);
            }
        }
    }
}
