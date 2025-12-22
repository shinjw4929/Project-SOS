using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class PlayerAuthoring : MonoBehaviour
    {
        public class Baker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                
                // 1. Player 식별 태그
                AddComponent(entity, new Player());

                // 2. 색상 변경용 컴포넌트
                AddComponent(entity, new URPMaterialPropertyBaseColor 
                { 
                    Value = new float4(1, 1, 1, 1) 
                });
            }
        }
    }
}