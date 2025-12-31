using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

/*
 * ProjectileDespawnSystem
 * - 실행 환경: ServerSimulation
 * - 역할:
 *   ProjectileMove.RemainingDistance가 0 이하가 된 투사체를 서버에서 제거한다.
 *
 * - 동작 순서:
 *   1) ProjectileMove를 가진 엔티티 중에서
 *   2) Projectile 태그가 붙어있고
 *   3) Prefab 자체가 아닌(실제 인스턴스인) 엔티티만 처리해서
 *   4) RemainingDistance가 0 이하이면 DestroyEntity로 제거한다.
 *
 * - 참고:
 *   Destroy/Instantiate 등은 메인 스레드에서 즉시 실행하지 않고 ECB(CommandBuffer)를 이용해
 *   EndSimulation 시점에서 일괄 반영한다.
 */
[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ProjectileDespawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // ECB �̱����� �غ���� ������ �� �ý����� ���� �ʵ��� �����Ѵ�.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // EndSimulation 시점에 반영될 CommandBuffer를 생성.
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // ���� ����ü �ν��Ͻ�(������ ����) �� RemainingDistance�� 0 ������ ��ƼƼ�� ã�� �����Ѵ�.
        foreach (var (move, entity) in
                 SystemAPI.Query<RefRO<ProjectileMove>>()
                          .WithAll<Projectile>()
                          .WithNone<Prefab>()
                          .WithEntityAccess())
        {
            // ���� �̵� ������ �Ÿ��� ���������� �����Ѵ�.
            if (move.ValueRO.RemainingDistance > 0f)
                continue;

            // RemainingDistance�� 0 ���ϰ� �Ǹ� �������� ����ü ��ƼƼ�� �����Ѵ�.
            ecb.DestroyEntity(entity);
        }
    }
}
