using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// PlayerResources 프리팹용 Authoring 컴포넌트.
    /// 빈 GameObject에 이 컴포넌트와 GhostAuthoringComponent를 붙여서 프리팹으로 만든다.
    /// </summary>
    public class UserResourcesAuthoring : MonoBehaviour
    {
        [Header("초기 자원 설정")]
        public int initialResources = 100;
        public int initialCurrentPopulation = 0;
        public int initialMaxPopulation = 300;

        class Baker : Baker<UserResourcesAuthoring>
        {
            public override void Bake(UserResourcesAuthoring authoring)
            {
                // 위치 정보가 필요 없는 데이터 전용 엔티티
                Entity entity = GetEntity(TransformUsageFlags.None);

                // 플레이어 자원 식별 태그
                AddComponent<UserResourcesTag>(entity);

                // 자원 데이터
                AddComponent(entity, new UserResources
                {
                    Resources = authoring.initialResources,
                    CurrentPopulation = authoring.initialCurrentPopulation,
                    MaxPopulation = authoring.initialMaxPopulation,
                });
            }
        }
    }
}