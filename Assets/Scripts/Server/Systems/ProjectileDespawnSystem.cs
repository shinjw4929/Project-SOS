using Unity.Entities;
using Unity.NetCode;

/*
 * ProjectileDespawnSystem
 * - 실행 월드: ServerSimulation
 * - 역할:
 *   ProjectileMove.RemainingDistance가 0 이하가 된 투사체를 서버에서 제거한다.
 *
 * - 동작 방식:
 *   1) ProjectileMove를 가진 엔티티 중에서
 *   2) Projectile 태그가 붙어있고
 *   3) Prefab 자체는 제외한(실제 인스턴스만) 엔티티를 대상으로
 *   4) RemainingDistance가 0 이하이면 DestroyEntity를 예약한다.
 *
 * - 주의:
 *   Destroy/Instantiate 같은 구조 변경은 즉시 실행하지 않고 ECB(CommandBuffer)에 기록한 뒤
 *   EndSimulation 구간에서 일괄 반영한다.
 *
 * - 중복 주의:
 *   만약 이동 시스템(ProjectileMoveServerSystem)에서도 RemainingDistance <= 0일 때 삭제를 하고 있다면
 *   이 시스템은 기능이 겹친다. 둘 중 하나만 유지하는 게 안전하다.
 */
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ProjectileDespawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // ECB 싱글톤이 준비되지 않으면 이 시스템은 돌지 않도록 제한한다.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // EndSimulation 시점에 반영될 CommandBuffer를 만든다.
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // 실제 투사체 인스턴스(프리팹 제외) 중 RemainingDistance가 0 이하인 엔티티를 찾아 제거한다.
        foreach (var (move, entity) in
                 SystemAPI.Query<RefRO<ProjectileMove>>()
                          .WithAll<Projectile>()
                          .WithNone<Prefab>()
                          .WithEntityAccess())
        {
            // 아직 이동 가능한 거리가 남아있으면 유지한다.
            if (move.ValueRO.RemainingDistance > 0f)
                continue;

            // RemainingDistance가 0 이하가 되면 서버에서 투사체 엔티티를 제거한다.
            ecb.DestroyEntity(entity);
        }
    }
}
