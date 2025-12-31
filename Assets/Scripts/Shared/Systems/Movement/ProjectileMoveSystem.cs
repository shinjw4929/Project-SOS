using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/*
 * ProjectileMoveServerSystem
 * - 실행 환경: ServerSimulation
 * - 역할:
 *   서버에서 투사체의 이동과 소멸을 관리하는 시스템.
 *
 * - 처리 흐름:
 *   1) ProjectileMove.Speed와 DeltaTime으로 이번 프레임 이동거리(step)를 계산한다.
 *   2) step이 RemainingDistance보다 크면 "남은 거리만큼만" 이동하도록 step을 줄인다.
 *   3) LocalTransform.Position을 Direction * step 만큼 이동한다.
 *   4) RemainingDistance에서 step을 빼서 "남은 이동 가능 거리"를 갱신한다.
 *   5) RemainingDistance가 0 이하가 되면 서버에서 투사체 엔티티를 제거(ECB 사용)한다.
 *
 * - 참고:
 *   DestroyEntity는 구조 변경이므로 ECB(CommandBuffer)를 사용하고 EndSimulation에서 반영한다.
 *   또한 Direction이 정규화되어 있다는 가정(길이 1)이 있어야 Speed가 의도대로 작동한다.
 */
[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ProjectileMoveServerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // ECB �̱����� ������ ���� ����(����)�� �����ϰ� ó���� �� �����Ƿ� ������Ʈ�� ���´�.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 현재 시뮬레이션의 델타 시간(초)
        float dt = SystemAPI.Time.DeltaTime;

        // EndSimulation ������ �ݿ��� CommandBuffer�� �����Ѵ�.
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        // ���� �ν��Ͻ��� ������� �̵� ó���Ѵ�(������ ��ƼƼ�� ����).
        foreach (var (tr, move, e) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ProjectileMove>>()
                                              .WithNone<Prefab>()
                                              .WithEntityAccess())
        {
            // �̹� �����ӿ� �̵��� �Ÿ� = �ӵ� * �ð�
            float step = move.ValueRO.Speed * dt;

            // ���� �̵� ���� �Ÿ����� �� �̵��Ϸ� �ϸ� ���� �Ÿ���ŭ�� �̵��Ѵ�.
            if (step > move.ValueRO.RemainingDistance)
                step = move.ValueRO.RemainingDistance;

            // ��ġ �̵�
            tr.ValueRW.Position += move.ValueRO.Direction * step;

            // ���� �Ÿ� ����
            move.ValueRW.RemainingDistance -= step;

            // ���� �Ÿ��� 0 ���ϰ� �Ǹ� ����ü ������ �����Ѵ�.
            if (move.ValueRO.RemainingDistance <= 0f)
                ecb.DestroyEntity(e);
        }
    }
}
