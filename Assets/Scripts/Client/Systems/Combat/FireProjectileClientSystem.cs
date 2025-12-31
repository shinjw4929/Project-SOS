using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

/*
 * FireProjectileClientSystem
 * - 실행 월드: ClientSimulation
 * - 역할:
 *   클라이언트에서 F 키 입력을 감지하고, "발사 요청"을 RPC로 서버에 전송한다.
 *
 * - 처리 흐름:
 *   1) F 키가 눌렸는지 확인한다. (눌리지 않았으면 아무 것도 하지 않는다)
 *   2) 현재 클라이언트가 InGame 상태인 서버 커넥션 엔티티(NetworkStreamInGame)를 찾는다.
 *      - NetCode에서 연결 엔티티는 보통 NetworkId + NetworkStreamInGame 컴포넌트를 가진다.
 *      - 찾지 못하면 아직 인게임 전이거나 연결이 끊긴 상태라서 RPC를 보낼 수 없다.
 *   3) 내 NetworkId를 가져온 뒤, 내 소유(GhostOwner.NetworkId == 내 NetworkId)인 고스트 엔티티 중 하나를 찾아
 *      발사 원점(origin)을 잡는다.
 *      - RTS 구조상 내 소유 엔티티가 여러 개일 수 있지만, 현재는 "일단 발사되게"가 목적이라
 *        첫 번째로 잡히는 엔티티를 사용한다.
 *   4) F를 누른 순간의 마우스 위치를 월드 좌표로 변환한다.
 *      - 카메라에서 마우스 방향으로 Ray를 쏘고
 *      - origin.y 높이의 수평 Plane과 교차하는 지점을 target으로 사용한다.
 *      - 이렇게 하면 "마우스 위치(바닥 기준)"으로 쏘는 좌표가 결정된다.
 *   5) FireProjectileRpc(Origin, Target) 엔티티를 생성하고, SendRpcCommandRequest를 붙여 서버로 전송한다.
 *
 * - 주의:
 *   이 시스템은 "투사체 생성/이동"을 하지 않는다.
 *   실제 투사체 Instantiate, 이동 데이터 세팅, 삭제 처리는 서버 시스템에서 한다.
 */
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct FireProjectileClientSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // 입력 트리거: F 키가 눌린 프레임에만 발사 요청을 만든다.
        if (!Keyboard.current.fKey.wasPressedThisFrame)
            return;

        // 카메라가 없으면 마우스 → 월드 좌표 변환을 할 수 없다.
        if (!Camera.main) // Unity Object는 implicit bool 사용
            return;

        // 1) InGame 상태의 연결 엔티티를 찾는다.
        //    NetworkStreamInGame이 붙은 커넥션만 RPC 전송 대상으로 삼는다.
        Entity connection = Entity.Null;
        foreach (var (_, e) in SystemAPI.Query<RefRO<NetworkId>>()
                                        .WithAll<NetworkStreamInGame>()
                                        .WithEntityAccess())
        {
            connection = e;
            break;
        }

        // 커넥션을 못 찾으면 아직 인게임 전이거나 연결이 끊긴 상태다.
        if (connection == Entity.Null)
        {
            Debug.LogWarning("FireProjectileClientSystem: InGame connection not found.");
            return;
        }

        // 내 네트워크 아이디(서버가 할당한 값)
        int myNetId = SystemAPI.GetComponent<NetworkId>(connection).Value;

        // 2) 내 소유 고스트 중 하나를 찾아 발사 원점을 잡는다.
        //    GhostOwner.NetworkId == myNetId 인 엔티티를 찾는다.
        Entity shooter = Entity.Null;
        float3 origin = default;

        foreach (var (owner, tr, e) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<LocalTransform>>()
                                                .WithEntityAccess())
        {
            if (owner.ValueRO.NetworkId != myNetId)
                continue;

            shooter = e;
            origin = tr.ValueRO.Position;
            break;
        }

        // 내 소유 엔티티가 없으면 발사 원점을 잡을 수 없다.
        if (shooter == Entity.Null)
        {
            Debug.LogWarning("FireProjectileClientSystem: shooter not found (no owned ghost).");
            return;
        }

        // 3) F를 누른 순간의 마우스 월드 위치를 구한다.
        //    origin.y 높이에서의 바닥 평면에 마우스 레이를 교차시켜 target을 만든다.
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane plane = new Plane(Vector3.up, new Vector3(0f, origin.y, 0f));

        if (!plane.Raycast(ray, out float enter))
            return;

        Vector3 hit = ray.GetPoint(enter);
        float3 target = new float3(hit.x, origin.y, hit.z);

        // 4) RPC 엔티티 생성 및 전송
        //    FireProjectileRpc는 서버에서 ReceiveRpcCommandRequest와 함께 처리된다.
        Entity rpcEntity = state.EntityManager.CreateEntity();

        state.EntityManager.AddComponentData(rpcEntity, new FireProjectileRpc
        {
            Origin = origin,
            Target = target
        });

        // TargetConnection에 InGame 커넥션을 지정해서 서버로 보내도록 한다.
        state.EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest
        {
            TargetConnection = connection
        });

        // 디버깅: 이 로그가 찍히면 클라이언트에서 RPC 전송 요청까지는 성공한 것이다.
        Debug.Log("FireProjectileClientSystem: RPC sent.");
    }
}
