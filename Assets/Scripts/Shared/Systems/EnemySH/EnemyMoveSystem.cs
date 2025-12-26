using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

// Enemy�� LastKnownPosition �������� �̵���Ű�� �ý���
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
            // Ÿ���� ������ �̵����� ����
            if (!target.ValueRO.HasTarget)
                continue;

            float3 dir =
                target.ValueRO.LastKnownPosition - transform.ValueRO.Position;

            // ���� ���� ��ġ�� �̵����� ����
            if (math.lengthsq(dir) < 0.0001f)
                continue;

            transform.ValueRW.Position +=
                math.normalize(dir) * config.ValueRO.MoveSpeed * dt;
        }
    }
}
