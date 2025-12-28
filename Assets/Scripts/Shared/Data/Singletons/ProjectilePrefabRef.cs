using Unity.Entities;

/*
 * ProjectilePrefabRef
 * - 역할:
 *   "투사체 프리팹 엔티티"를 담아두는 ECS 컴포넌트.
 *   보통 월드에 1개만 존재하는 싱글톤 형태로 사용한다.
 *
 * - 누가 만들고 넣는가:
 *   ProjectilePrefabRefAuthoring의 Baker가 베이킹 단계에서 생성/추가한다.
 *   즉, 인스펙터에서 지정한 GameObject 프리팹이 Entity 프리팹으로 변환된 뒤,
 *   그 Entity가 Prefab 필드에 저장된다.
 *
 * - 누가 사용하는가:
 *   서버에서 투사체를 생성하는 시스템(FireProjectileServerSystem)이
 *   SystemAPI.GetSingleton<ProjectilePrefabRef>().Prefab 으로 읽어와서
 *   ecb.Instantiate(prefab) 또는 EntityManager.Instantiate(prefab)에 사용한다.
 *
 * - 주의:
 *   이 컴포넌트가 월드에 없으면 서버는 프리팹을 모르는 상태라 투사체를 생성할 수 없다.
 *   그래서 보통 서버 시스템 OnCreate에서 RequireForUpdate<ProjectilePrefabRef>()를 걸어둔다.
 */
public struct ProjectilePrefabRef : IComponentData
{
    // Instantiate에 사용할 Entity 프리팹(베이킹된 프리팹 엔티티)
    public Entity Prefab;
}
