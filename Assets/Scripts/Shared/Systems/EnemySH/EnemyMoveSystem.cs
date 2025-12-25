using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Enemy를 LastKnownPosition 방향으로 이동시키는 시스템
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct EnemyMoveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (transform, target, config) in
            SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRO<EnemyTarget>,
                RefRO<EnemyFollowConfig>>())
        {
            // 타겟이 없으면 이동하지 않음
            if (!target.ValueRO.HasTarget)
                continue;

            float3 dir =
                target.ValueRO.LastKnownPosition - transform.ValueRO.Position;

            // 거의 같은 위치면 이동하지 않음
            if (math.lengthsq(dir) < 0.0001f)
                continue;

            transform.ValueRW.Position +=
                math.normalize(dir) * config.ValueRO.MoveSpeed * dt;
        }
    }
}
