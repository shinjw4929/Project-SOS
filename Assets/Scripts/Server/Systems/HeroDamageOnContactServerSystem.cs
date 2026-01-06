using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

// 서버에서만 HP를 깎는다.
// 물리 이벤트(Entities Physics)가 없어도 동작하도록, 거리로 "닿음"을 판정한다.
// 닿는 순간 1회 적용되고, 이후 Interval 주기로 반복 적용된다.
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct HeroDamageOnContactServerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        var enemyQuery = SystemAPI.QueryBuilder()
            .WithAll<ContactDamage, LocalTransform>()
            .Build();

        if (enemyQuery.IsEmptyIgnoreFilter)
            return;

        using var enemyEntities = enemyQuery.ToEntityArray(Allocator.Temp);
        using var enemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        using var enemyDamages = enemyQuery.ToComponentDataArray<ContactDamage>(Allocator.Temp);

        foreach (var (health, cooldown, heroTransform, heroEntity) in SystemAPI
                     .Query<RefRW<HeroHealth>, RefRW<HeroDamageCooldown>, RefRO<LocalTransform>>()
                     .WithAll<HeroTag>()
                     .WithEntityAccess())
        {
            float timeLeft = math.max(0f, cooldown.ValueRW.TimeLeft - dt);

            int totalDamage = 0;
            float minInterval = float.MaxValue;

            float3 heroPos = heroTransform.ValueRO.Position;

            for (int i = 0; i < enemyEntities.Length; i++)
            {
                var cd = enemyDamages[i];
                if (cd.Range <= 0f)
                    continue;

                float3 enemyPos = enemyTransforms[i].Position;

                float r = cd.Range;
                float distSq = math.distancesq(heroPos, enemyPos);

                if (distSq <= r * r)
                {
                    totalDamage += math.max(0, cd.Damage);
                    minInterval = math.min(minInterval, cd.Interval);
                }
            }

            if (totalDamage <= 0)
            {
                cooldown.ValueRW.TimeLeft = timeLeft;
                continue;
            }

            if (timeLeft <= 0f)
            {
                int before = health.ValueRW.Current;
                int after = math.max(0, before - totalDamage);
                health.ValueRW.Current = after;

                float next = (minInterval == float.MaxValue) ? 0f : math.max(0f, minInterval);
                cooldown.ValueRW.TimeLeft = next;

#if UNITY_EDITOR
                if (before != after)
                    Debug.Log($"[Server] Hero HP {before} -> {after} (damage={totalDamage}, interval={next:0.00})");
#endif
#if UNITY_EDITOR
Debug.Log($"[Server] Hero HP {before} -> {after} (damage={totalDamage}, interval={next:0.00})");
#endif
            }
            else
            {
                cooldown.ValueRW.TimeLeft = timeLeft;
            }
        }
    }
}
