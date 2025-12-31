using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

/*
 * FireProjectileServerSystem
 * - 실행 환경: ServerSimulation
 * - 역할:
 *   클라이언트가 보낸 FireProjectileRpc를 서버에서 처리해서 투사체를 생성(Instantiate)하고,
 *   투사체의 초기 위치(LocalTransform)와 이동 데이터(ProjectileMove)를 설정한다.
 *
 * - 입력(클라이언트에서 전송):
 *   FireProjectileRpc
 *     - Origin: 발사 시작 위치(유닛 위치)
 *     - Target: F키 누른 순간의 마우스 월드 좌표(바닥 기준)
 *
 * - 출력(서버에서 생성하는 것):
 *   Projectile 프리팹 엔티티를 Instantiate 해서 실제 투사체 엔티티를 만든다.
 *
 * - 거리/속도 관련 포인트:
 *   ProjectileMove.Speed            : 투사체 속도
 *   ProjectileMove.RemainingDistance: 투사체가 생성된 후 이동할 수 있는 최대 거리
 *
 * - 구조 변경 참고:
 *   Instantiate/DestroyEntity는 ECB를 사용하고 EndSimulation에서 반영한다.
 */
[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct FireProjectileServerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // �������� ����ü ������ ����(�̱���)�� �غ���� ������ �ý����� ������ �ʴ´�.
        state.RequireForUpdate<ProjectilePrefabRef>();

        // ECB �̱����� �־�� Instantiate/Destroy�� �����ϰ� ������ �� �ִ�.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // EndSimulation ������ �ݿ��� CommandBuffer�� �����Ѵ�.
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        // ������ ��� �ִ� ����ü ������ ��ƼƼ�� �����´�.
        Entity prefab = SystemAPI.GetSingleton<ProjectilePrefabRef>().Prefab;

        // Ŭ���̾�Ʈ�� ���� RPC(�߻� ��û)�� ó���Ѵ�.
        // ReceiveRpcCommandRequest�� "�� ��ƼƼ�� RPC ���ſ��̴�"�� ��Ÿ���� NetCode ������Ҵ�.
        foreach (var (rpc, req, e) in SystemAPI.Query<RefRO<FireProjectileRpc>, RefRO<ReceiveRpcCommandRequest>>()
                                              .WithEntityAccess())
        {
            // RPC�� ���Ե� �߻� ������ ��ǥ ����
            float3 origin = rpc.ValueRO.Origin;
            float3 target = rpc.ValueRO.Target;

            // �߻� ���� ���: (Target - Origin)�� ����ȭ
            // normalizesafe�� ���̰� 0�� ������ 0 ���Ͱ� ���� �� �����Ƿ� �Ʒ����� �����Ѵ�.
            float3 dir = math.normalizesafe(target - origin);

            // ������ ���� 0�̸� �⺻ ������ �ش�(0���� ����)
            if (math.lengthsq(dir) < 0.0001f)
                dir = new float3(0, 0, 1);

            // ���� ��ġ ����:
            // - dir �������� ��¦ ������(���� �� ��)
            // - y�� �ణ �÷� �ٴڿ� ������ ������ ��ȭ
            float3 spawnPos = origin + dir * 1.0f;
            spawnPos.y = origin.y + 0.5f;

            // ����ü �ν��Ͻ� ����(������ �� ���� ��ƼƼ)
            Entity proj = ecb.Instantiate(prefab);

            // ��ġ(LocalTransform) �ʱ�ȭ
            ecb.SetComponent(proj, LocalTransform.FromPosition(spawnPos));

            // �̵� ������ ����:
            // - Direction: ���ư� ����
            // - Speed: �ʴ� �̵� �Ÿ�
            // - RemainingDistance: ������ �� �̵� ������ �ִ� �Ÿ�(0 ���ϰ� �Ǹ� ���� ���)
            ecb.SetComponent(proj, new ProjectileMove
            {
                Direction = dir,
                Speed = 20f,
                RemainingDistance = 30f
            });

            // RPC ��ƼƼ�� 1ȸ ó�� �� �����Ѵ�(��� ���������� �� ������ ��ó�� ����).
            ecb.DestroyEntity(e);

            // ���� �α�: ���� ������ �߻��ߴ��� Ȯ�ο�
            Debug.Log("FireProjectileServerSystem: projectile spawned.");
        }
    }
}
