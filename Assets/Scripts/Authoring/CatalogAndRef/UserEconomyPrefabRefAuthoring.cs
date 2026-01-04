using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// PlayerEconomy 프리팹 참조용 Authoring 컴포넌트.
    /// SubScene 내 아무 GameObject에 붙이고, playerEconomyPrefab 슬롯에 프리팹을 할당한다.
    /// </summary>
    public class UserEconomyPrefabRefAuthoring : MonoBehaviour
    {
        [Tooltip("UserEconomyAuthoring + GhostAuthoringComponent가 붙은 프리팹")]
        public GameObject playerEconomyPrefab;

        class Baker : Baker<UserEconomyPrefabRefAuthoring>
        {
            public override void Bake(UserEconomyPrefabRefAuthoring authoring)
            {
                if (authoring.playerEconomyPrefab == null)
                {
                    Debug.LogWarning("PlayerEconomyPrefabRefAuthoring: 프리팹이 할당되지 않았습니다.");
                    return;
                }

                // 프리팹을 Entity로 변환 (위치 불필요)
                Entity prefabEntity = GetEntity(authoring.playerEconomyPrefab, TransformUsageFlags.None);

                // 싱글톤 엔티티 생성
                Entity singleton = GetEntity(TransformUsageFlags.None);

                AddComponent(singleton, new UserEconomyPrefabRef
                {
                    Prefab = prefabEntity
                });
            }
        }
    }
}