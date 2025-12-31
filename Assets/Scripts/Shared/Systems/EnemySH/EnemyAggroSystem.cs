using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Shared.Systems
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct EnemyAggroSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 플레이어(Hero) 쿼리
            var heroQuery = SystemAPI.QueryBuilder()
                .WithAll<HeroTag, LocalTransform, Team>()
                .Build();

            if (heroQuery.IsEmpty)
                return;

            var heroes = heroQuery.ToEntityArray(state.WorldUpdateAllocator);
            var heroTransforms =
                heroQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);
            var heroTeams =
                heroQuery.ToComponentDataArray<Team>(state.WorldUpdateAllocator);

            // 모든 Enemy 순회
            foreach (var (enemyTransform, target, enemyTeam)
                     in SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRW<Target>,
                         RefRO<Team>>())
            {
                float closestDistSq = float.MaxValue;
                Entity closestHero = Entity.Null;

                for (int i = 0; i < heroes.Length; i++)
                {
                    // 같은 팀 제외
                    if (heroTeams[i].teamId == enemyTeam.ValueRO.teamId)
                        continue;

                    float distSq = math.distancesq(
                        enemyTransform.ValueRO.Position,
                        heroTransforms[i].Position
                    );

                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestHero = heroes[i];
                    }
                }

                if (closestHero != Entity.Null)
                {
                    target.ValueRW.TargetEntity = closestHero;
                }
            }
        }
    }
}
