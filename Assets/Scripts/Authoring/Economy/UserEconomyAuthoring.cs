using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// UserEconomy 프리팹용 Authoring 컴포넌트.
    /// 빈 GameObject에 이 컴포넌트와 GhostAuthoringComponent를 붙여서 프리팹으로 만든다.
    /// </summary>
    public class UserEconomyAuthoring : MonoBehaviour
    {
        [Header("초기 자원 설정")]
        public int initialCurrency = 100;
        public int initialCurrentPopulation = 0;
        public int initialMaxPopulation = 300;

        class Baker : Baker<UserEconomyAuthoring>
        {
            public override void Bake(UserEconomyAuthoring authoring)
            {
                // 위치 정보가 필요 없는 데이터 전용 엔티티
                Entity entity = GetEntity(TransformUsageFlags.None);

                // 플레이어 자원 식별 태그
                AddComponent<UserEconomyTag>(entity);

                // 자원 데이터
                AddComponent(entity, new UserCurrency
                {
                    Amount = authoring.initialCurrency,
                });
                
                AddComponent(entity, new UserSupply
                {
                    Currentvalue = authoring.initialCurrentPopulation,
                    MaxValue = authoring.initialMaxPopulation,
                });
            }
        }
    }
}