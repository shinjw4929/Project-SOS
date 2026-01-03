using Unity.Entities;
using Unity.Mathematics;

/*
 * ProjectileMove
 * - 역할:
 *   투사체가 "어느 방향으로, 어느 속도로, 얼마나 더 이동할 수 있는지"를 담는 이동 데이터 컴포넌트.
 *
 * - 사용처(흐름):
 *   1) 서버 스폰 시스템(FireProjectileServerSystem 등)에서 초기값을 세팅한다.
 *      - Direction: 발사 방향(보통 normalize(Target - Origin))
 *      - Speed: 초당 이동 거리(월드 단위/초)
 *      - RemainingDistance: 앞으로 이동 가능한 총 거리(예: 1000f)
 *
 *   2) 이동 시스템(ProjectileMoveServerSystem 등)에서 매 틱마다 다음을 수행한다.
 *      - step = Speed * dt
 *      - Position += Direction * step
 *      - RemainingDistance -= step
 *
 *   3) RemainingDistance가 0 이하가 되면 디스폰 시스템 또는 이동 시스템에서 엔티티를 삭제한다.
 *
 * - 주의:
 *   Direction은 방향 벡터이므로 길이가 1에 가깝게(normalize) 세팅하는 것이 안전하다.
 *   (길이가 크면 속도와 곱해져 이동량이 의도보다 커질 수 있다.)
 */
public struct ProjectileMove : IComponentData
{
    // 투사체가 이동할 방향 벡터(보통 정규화된 값)
    public float3 Direction;

    // 초당 이동 거리(월드 단위/초)
    public float Speed;

    // 앞으로 더 이동 가능한 거리(0 이하가 되면 삭제 대상)
    public float RemainingDistance;
}
