using UnityEngine;
using Unity.Entities;

namespace Shared
{
    // struct가 아닌 class로 선언하여 Managed Component로 만듭니다.
    // 이를 통해 GameObject 참조를 저장할 수 있습니다.
    public class StructurePrefabStore : IComponentData
    {
        public GameObject[] Prefabs;
    }
}