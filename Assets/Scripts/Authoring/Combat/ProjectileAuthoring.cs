using Unity.Entities;
using UnityEngine;

public class ProjectileAuthoring : MonoBehaviour
{
    class Baker : Baker<ProjectileAuthoring>
    {
        public override void Bake(ProjectileAuthoring authoring)
        {
            /*
             * Projectile 프리팹은 실제로 움직이며 화면에 렌더되어야 하므로
             * Dynamic + Renderable 플래그를 함께 사용합니다.
             *
             * Renderable을 안 넣으면 Entities Graphics가 렌더 데이터를 베이킹하지 않아
             * 엔티티는 움직이는데 화면에는 안 보이는 상태가 발생할 수 있습니다.
             */
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);

            /*
             * 발사체 기본 컴포넌트
             * ProjectileMove는 이동 시스템이 읽어서 LocalTransform을 갱신합니다.
             */
            AddComponent<Projectile>(entity);
            AddComponent<ProjectileMove>(entity);

            /*
             * 주의:
             * ProjectilePrefabTag / ProjectilePrefabInitSystem 방식은 사용하지 않습니다.
             * 프리팹 참조는 ProjectilePrefabRef(ProjectilePrefabAuthoring)가 Singleton(ProjectilePrefab)을 제공합니다.
             */
        }
    }
}
