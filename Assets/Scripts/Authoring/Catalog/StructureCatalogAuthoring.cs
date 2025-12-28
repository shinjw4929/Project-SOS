using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    // 건물 목록 저장소
    public class StructureCatalogAuthoring : MonoBehaviour
    {
        public List<GameObject> prefabs; 

        public class Baker : Baker<StructureCatalogAuthoring>
        {
            public override void Bake(StructureCatalogAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                
                // [식별용] 태그 컴포넌트
                AddComponent(entity, new StructureCatalog());

                // 버퍼 생성 및 프리팹 담기
                var buffer = AddBuffer<StructureCatalogElement>(entity);
                
                foreach (var prefabObj in authoring.prefabs)
                {
                    // null이면 Entity.Null, 아니면 변환된 Entity 저장
                    Entity prefabEntity = prefabObj != null ? GetEntity(prefabObj, TransformUsageFlags.Dynamic) : Entity.Null;
                    buffer.Add(new StructureCatalogElement { PrefabEntity = prefabEntity });
                }
            }
        }
    }
}