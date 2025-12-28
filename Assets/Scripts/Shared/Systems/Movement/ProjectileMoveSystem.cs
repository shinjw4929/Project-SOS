using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/*
 * ProjectileMoveServerSystem
 * - 실행 월드: ServerSimulation
 * - 역할:
 *   서버에서 투사체의 이동을 실제로 진행시키는 시스템.
 *
 * - 처리 흐름:
 *   1) ProjectileMove.Speed와 DeltaTime으로 이번 프레임 이동거리(step)를 계산한다.
 *   2) step이 RemainingDistance보다 커지면 "남은 거리만큼만" 이동하도록 step을 줄인다.
 *      (남은 거리보다 더 이동해서 음수가 되는 것, 마지막 프레임에 튀는 것 방지)
 *   3) LocalTransform.Position을 Direction * step 만큼 이동한다.
 *   4) RemainingDistance에서 step을 빼서 "남은 이동 가능 거리"를 갱신한다.
 *   5) RemainingDistance가 0 이하가 되면 서버에서 투사체 엔티티를 제거(ECB 예약)한다.
 *
 * - 주의:
 *   DestroyEntity는 구조 변경이므로 ECB(CommandBuffer)에 기록하고 EndSimulation에서 반영한다.
 *   또한 Direction이 정규화되어 있다는 전제(길이 1)가 있어야 Speed가 의도대로 작동한다.
 */
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ProjectileMoveServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // ECB 싱글톤이 없으면 구조 변경(삭제)을 안전하게 처리할 수 없으므로 업데이트를 막는다.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // 서버 시뮬레이션의 프레임 시간(초)
        float dt = SystemAPI.Time.DeltaTime;

        // EndSimulation 시점에 반영될 CommandBuffer를 생성한다.
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        // 실제 인스턴스만 대상으로 이동 처리한다(프리팹 엔티티는 제외).
        foreach (var (tr, move, e) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ProjectileMove>>()
                                              .WithNone<Prefab>()
                                              .WithEntityAccess())
        {
            // 이번 프레임에 이동할 거리 = 속도 * 시간
            float step = move.ValueRO.Speed * dt;

            // 남은 이동 가능 거리보다 더 이동하려 하면 남은 거리만큼만 이동한다.
            if (step > move.ValueRO.RemainingDistance)
                step = move.ValueRO.RemainingDistance;

            // 위치 이동
            tr.ValueRW.Position += move.ValueRO.Direction * step;

            // 남은 거리 차감
            move.ValueRW.RemainingDistance -= step;

            // 남은 거리가 0 이하가 되면 투사체 삭제를 예약한다.
            if (move.ValueRO.RemainingDistance <= 0f)
                ecb.DestroyEntity(e);
        }
    }
}
