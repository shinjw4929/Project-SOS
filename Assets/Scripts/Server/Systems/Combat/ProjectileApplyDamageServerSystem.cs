using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Shared;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
public partial struct ProjectileApplyDamageServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var sim = SystemAPI.GetSingleton<SimulationSingleton>();

        var hpLookup = SystemAPI.GetComponentLookup<Health>(false);
        var projLookup = SystemAPI.GetComponentLookup<Projectile>(true);
        var dmgLookup = SystemAPI.GetComponentLookup<ProjectileDamageData>(true);

        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        var processed = new NativeParallelHashSet<Entity>(1024, Allocator.TempJob);

        var job = new TriggerJob
        {
            HpLookup = hpLookup,
            ProjLookup = projLookup,
            DmgLookup = dmgLookup,
            ECB = ecb,
            Processed = processed.AsParallelWriter()
        };

        state.Dependency = job.Schedule(sim, state.Dependency);
        state.Dependency.Complete();

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        processed.Dispose();
    }

    [BurstCompile]
    private struct TriggerJob : ITriggerEventsJob
    {
        public ComponentLookup<Health> HpLookup;
        [ReadOnly] public ComponentLookup<Projectile> ProjLookup;
        [ReadOnly] public ComponentLookup<ProjectileDamageData> DmgLookup;

        public EntityCommandBuffer ECB;
        public NativeParallelHashSet<Entity>.ParallelWriter Processed;

        public void Execute(TriggerEvent e)
        {
            var a = e.EntityA;
            var b = e.EntityB;

            bool aProj = ProjLookup.HasComponent(a);
            bool bProj = ProjLookup.HasComponent(b);
            if (!aProj && !bProj) return;

            var proj = aProj ? a : b;
            var other = aProj ? b : a;

            // EnemyHealthData 가진 애만 맞으면 깎음
            if (!HpLookup.HasComponent(other)) return;
            if (!DmgLookup.HasComponent(proj)) return;

            // 같은 프레임 다중 트리거 방지
            if (!Processed.Add(proj)) return;

            var hp = HpLookup[other];
            int dmg = DmgLookup[proj].Value;
            if (dmg <= 0) dmg = 1;

            hp.CurrentValue -= dmg;
            if (hp.CurrentValue < 0) hp.CurrentValue = 0;

            HpLookup[other] = hp;

            // 투사체만 제거 (적 제거는 여기서 안 함)
            ECB.DestroyEntity(proj);
        }
    }
}
