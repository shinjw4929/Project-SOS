using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class MoveTargetAuthoring : MonoBehaviour
    {
        public class Baker : Baker<MoveTargetAuthoring>
        {
            public override void Bake(MoveTargetAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // 1. 서버 동기화용 (결과물)
                AddComponent(entity, new MoveTarget { position = float3.zero, isValid = false });

                // 2. 명령 전송용 버퍼
                AddBuffer<RTSCommand>(entity);

                // 3. [핵심 추가] 클라이언트 입력 기억용 (초기값 0)
                AddComponent(entity, new RTSInputState { TargetPosition = float3.zero, HasTarget = false });
            }
        }
    }
}