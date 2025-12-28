using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

/*
 * FireProjectileServerSystem
 * - 실행 월드: ServerSimulation
 * - 역할:
 *   클라이언트가 보낸 FireProjectileRpc를 서버에서 수신해서 투사체를 생성(Instantiate)하고,
 *   투사체의 초기 위치(LocalTransform)와 이동 데이터(ProjectileMove)를 세팅한다.
 *
 * - 입력(클라이언트 → 서버):
 *   FireProjectileRpc
 *     - Origin: 발사 기준 위치(보통 유닛 위치)
 *     - Target: F를 누른 순간의 마우스 월드 좌표(바닥 평면 기준)
 *
 * - 출력(서버에서 생성하는 것):
 *   Projectile 프리팹 엔티티를 Instantiate 해서 실제 투사체 엔티티를 만든다.
 *
 * - 거리/속도 조절 포인트:
 *   ProjectileMove.Speed            : 투사체 속도
 *   ProjectileMove.RemainingDistance: 투사체가 앞으로 더 이동할 수 있는 최대 거리
 *
 * - 구조 변경 주의:
 *   Instantiate/DestroyEntity는 ECB에 기록하고 EndSimulation에서 반영한다.
 */
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct FireProjectileServerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // 서버에서 투사체 프리팹 참조(싱글톤)가 준비되지 않으면 시스템을 돌리지 않는다.
        state.RequireForUpdate<ProjectilePrefabRef>();

        // ECB 싱글톤이 있어야 Instantiate/Destroy를 안전하게 예약할 수 있다.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // EndSimulation 시점에 반영될 CommandBuffer를 생성한다.
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        // 서버가 들고 있는 투사체 프리팹 엔티티를 가져온다.
        Entity prefab = SystemAPI.GetSingleton<ProjectilePrefabRef>().Prefab;

        // 클라이언트가 보낸 RPC(발사 요청)를 처리한다.
        // ReceiveRpcCommandRequest는 "이 엔티티가 RPC 수신용이다"를 나타내는 NetCode 구성요소다.
        foreach (var (rpc, req, e) in SystemAPI.Query<RefRO<FireProjectileRpc>, RefRO<ReceiveRpcCommandRequest>>()
                                              .WithEntityAccess())
        {
            // RPC에 포함된 발사 원점과 목표 지점
            float3 origin = rpc.ValueRO.Origin;
            float3 target = rpc.ValueRO.Target;

            // 발사 방향 계산: (Target - Origin)을 정규화
            // normalizesafe는 길이가 0에 가까우면 0 벡터가 나올 수 있으므로 아래에서 보정한다.
            float3 dir = math.normalizesafe(target - origin);

            // 방향이 거의 0이면 기본 방향을 준다(0벡터 방지)
            if (math.lengthsq(dir) < 0.0001f)
                dir = new float3(0, 0, 1);

            // 스폰 위치 보정:
            // - dir 방향으로 살짝 앞으로(유닛 몸 앞)
            // - y를 약간 올려 바닥에 박히는 문제를 완화
            float3 spawnPos = origin + dir * 1.0f;
            spawnPos.y = origin.y + 0.5f;

            // 투사체 인스턴스 생성(프리팹 → 실제 엔티티)
            Entity proj = ecb.Instantiate(prefab);

            // 위치(LocalTransform) 초기화
            ecb.SetComponent(proj, LocalTransform.FromPosition(spawnPos));

            // 이동 데이터 세팅:
            // - Direction: 날아갈 방향
            // - Speed: 초당 이동 거리
            // - RemainingDistance: 앞으로 더 이동 가능한 최대 거리(0 이하가 되면 삭제 대상)
            ecb.SetComponent(proj, new ProjectileMove
            {
                Direction = dir,
                Speed = 20f,
                RemainingDistance = 30f
            });

            // RPC 엔티티는 1회 처리 후 제거한다(계속 남아있으면 매 프레임 재처리 위험).
            ecb.DestroyEntity(e);

            // 서버 로그: 실제 스폰이 발생했는지 확인용
            Debug.Log("FireProjectileServerSystem: projectile spawned.");
        }
    }
}
