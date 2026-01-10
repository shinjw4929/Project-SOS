using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    // 유닛 목록 저장소
    public class UnitCatalogAuthoring : MonoBehaviour
    {
        public List<GameObject> prefabs; 

        public class Baker : Baker<UnitCatalogAuthoring>
        {
            public override void Bake(UnitCatalogAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                
                // [식별용] 싱글톤 태그 컴포넌트
                AddComponent(entity, new UnitCatalog());

                // 버퍼 생성 및 프리팹 담기
                var buffer = AddBuffer<UnitCatalogElement>(entity);
                
                foreach (var prefabObj in authoring.prefabs)
                {
                    // null이면 Entity.Null, 아니면 변환된 Entity 저장
                    Entity prefabEntity = prefabObj != null ? GetEntity(prefabObj, TransformUsageFlags.Dynamic) : Entity.Null;
                    buffer.Add(new UnitCatalogElement { PrefabEntity = prefabEntity });
                }
            }
        }
    }
}
