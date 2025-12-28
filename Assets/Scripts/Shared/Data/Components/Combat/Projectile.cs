using Unity.Entities;

/*
 * Projectile
 * - 역할:
 *   "이 엔티티는 투사체다"를 표시하는 태그 컴포넌트.
 *   데이터가 없는 IComponentData는 보통 분류/필터링 목적의 마커로 사용한다.
 *
 * - 왜 필요한가:
 *   시스템에서 투사체만 골라 처리하고 싶을 때 WithAll<Projectile>() 같은 필터로 쉽게 걸러낼 수 있다.
 *   예:
 *     - ProjectileMoveServerSystem: 투사체 이동 처리
 *     - ProjectileDespawnSystem: 투사체 삭제 조건 처리
 *     - 향후 충돌/피해 처리 시스템: 투사체만 대상으로 검사
 *
 * - 어디에 붙는가:
 *   투사체 프리팹(Projectile 프리팹)의 베이커(ProjectileAuthoring 등)에서 AddComponent<Projectile>()로 붙인다.
 *   서버가 ecb.Instantiate(prefab)로 생성하면, 프리팹에 붙어있던 Projectile 태그도 같이 복제된다.
 *
 * - 주의:
 *   태그라서 상태값은 없다.
 *   속도/방향/남은거리 같은 실제 데이터는 ProjectileMove 같은 별도 컴포넌트가 담당한다.
 */
public struct Projectile : IComponentData
{
}
