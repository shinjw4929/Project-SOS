using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

/*
 * FireProjectileRpc
 * - 역할:
 *   클라이언트가 "발사 요청"을 서버로 보내기 위해 사용하는 RPC 데이터 구조체.
 *   IRpcCommand를 구현하면 NetCode가 이 컴포넌트를 RPC로 취급한다.
 *
 * - 생성/전송 위치(클라이언트):
 *   FireProjectileClientSystem에서 다음 순서로 사용한다.
 *     1) rpcEntity = EntityManager.CreateEntity()
 *     2) rpcEntity에 FireProjectileRpc(Origin, Target) 추가
 *     3) rpcEntity에 SendRpcCommandRequest(TargetConnection=connection) 추가
 *
 * - 수신/처리 위치(서버):
 *   FireProjectileServerSystem 같은 서버 시스템에서 다음 쿼리로 받는다.
 *     Query<RefRO<FireProjectileRpc>, RefRO<ReceiveRpcCommandRequest>>()
 *   그리고 rpcEntity는 1회 처리 후 DestroyEntity로 제거한다.
 *
 * - 필드 의미:
 *   Origin:
 *     발사 원점(보통 발사자/유닛 위치). 클라이언트가 발사 시점에 잡은 좌표.
 *
 *   Target:
 *     발사 시점에 마우스가 가리키는 월드 좌표(바닥 평면 기준).
 *     서버는 (Target - Origin)으로 방향을 계산한다.
 *
 * - 주의:
 *   이 RPC는 "입력/요청"만 전달한다.
 *   실제 투사체 생성(Instantiate), 이동 데이터 세팅, 삭제 처리는 서버에서 한다.
 */
public struct FireProjectileRpc : IRpcCommand
{
    public float3 TargetPosition; // 마우스 월드 좌표
}