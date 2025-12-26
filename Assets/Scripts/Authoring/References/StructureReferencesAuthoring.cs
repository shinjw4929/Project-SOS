using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class StructureReferencesAuthoring : MonoBehaviour
    {
        // 이제 변수 하나하나 선언하지 않고 리스트로 관리합니다.
        // Inspector에서 Enum 순서에 맞춰 프리팹을 드래그해서 넣으세요.
        public List<GameObject> buildingPrefabs; 

        public class Baker : Baker<StructureReferencesAuthoring>
        {
            public override void Bake(StructureReferencesAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                
                // 싱글톤 태그 컴포넌트 (식별용)
                AddComponent(entity, new StructureEntitiesReferences());

                // 버퍼 생성 및 프리팹 담기
                var buffer = AddBuffer<StructurePrefabElement>(entity);
                
                foreach (var prefabObj in authoring.buildingPrefabs)
                {
                    // null이면 Entity.Null, 아니면 변환된 Entity 저장
                    Entity prefabEntity = prefabObj != null ? GetEntity(prefabObj, TransformUsageFlags.Dynamic) : Entity.Null;
                    buffer.Add(new StructurePrefabElement { PrefabEntity = prefabEntity });
                }
            }
        }
    }
}