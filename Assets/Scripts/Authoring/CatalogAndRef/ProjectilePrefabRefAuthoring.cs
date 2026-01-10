using Unity.Entities;
using UnityEngine;

/*
 * ProjectilePrefabRefAuthoring
 *   "투사체 프리팹 엔티티"를 ECS 월드에서 참조할 수 있도록 싱글톤 컴포넌트(ProjectilePrefabRef)를 만들어 주는 브릿지.
 *
 *   서버 시스템(FireProjectileServerSystem)은 Entity prefab을 알아야 Instantiate(prefab)를 할 수 있다.
 *   그런데 ECS 시스템은 UnityEngine.GameObject 프리팹을 직접 물고 있을 수 없으니,
 *   베이킹 단계에서 GameObject 프리팹을 Entity 프리팹으로 변환한 뒤, 그 Entity를 싱글톤으로 저장해 둔다.
 */
public class ProjectilePrefabRefAuthoring : MonoBehaviour
{
    // 인스펙터에서 할당하는 투사체 GameObject 프리팹
    public GameObject projectilePrefab;

    /*
     * Baker
     * - 역할:
     *   GameObject 기반의 authoring 데이터를 ECS 엔티티/컴포넌트로 변환한다.
     */
    class Baker : Baker<ProjectilePrefabRefAuthoring>
    {
        public override void Bake(ProjectilePrefabRefAuthoring authoring)
        {
            // 프리팹이 할당되지 않으면 싱글톤을 만들 수 없으니 조용히 종료한다.
            // (프로젝트 정책에 따라 Debug.LogWarning을 넣어도 되지만, 베이킹 로그는 과해질 수 있다)
            if (!authoring.projectilePrefab)
                return;

            // GameObject 프리팹을 "Entity 프리팹"으로 변환/참조한다.
            // TransformUsageFlags.Dynamic:
            //   투사체는 런타임에 이동하므로, 동적 트랜스폼을 쓰는 엔티티로 베이크되도록 한다.
            Entity prefabEntity = GetEntity(authoring.projectilePrefab, TransformUsageFlags.Dynamic);

            // 이 Authoring 컴포넌트가 붙은 GameObject 자체를 대표하는 엔티티를 가져온다.
            // TransformUsageFlags.None:
            //   이 엔티티는 위치/회전이 필요 없는 "참조용 싱글톤"이므로 트랜스폼이 없어도 된다.
            Entity singleton = GetEntity(TransformUsageFlags.None);

            // 서버에서 접근할 수 있도록 ProjectilePrefabRef(프리팹 엔티티 참조)를 싱글톤 엔티티에 추가한다.
            AddComponent(singleton, new ProjectilePrefabRef
            {
                Prefab = prefabEntity
            });
        }
    }
}
