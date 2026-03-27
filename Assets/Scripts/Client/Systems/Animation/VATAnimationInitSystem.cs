using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// 새 메시 엔티티에 VATAnimParam + VATAnimTarget + PreviousClipIndex를 부착한다.
    /// TeamColorSystem의 초기화 패턴과 동일: Parent 체인 탐색으로 VATAnimationState 보유 루트 엔티티를 찾는다.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(VATAnimationPlaybackSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct VATAnimationInitSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VATAnimationState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool hasNew = false;

            foreach (var (parent, entity) in
                SystemAPI.Query<RefRO<Parent>>()
                    .WithAll<MaterialMeshInfo>()
                    .WithNone<VATAnimTarget>()
                    .WithEntityAccess())
            {
                Entity rootEntity = FindVATAncestor(parent.ValueRO.Value, ref state);

                // VAT 유무와 무관하게 VATAnimTarget 부착 (재쿼리 방지, TeamColorSystem 패턴)
                ecb.AddComponent(entity, new VATAnimTarget { RootEntity = rootEntity });

                if (rootEntity != Entity.Null)
                {
                    ecb.AddComponent(entity, new VATAnimParam { Value = float4.zero });
                    ecb.AddComponent(entity, new PreviousClipIndex { Value = 0 });
                }

                hasNew = true;
            }

            if (hasNew)
            {
                ecb.Playback(state.EntityManager);
            }
            ecb.Dispose();
        }

        private Entity FindVATAncestor(Entity current, ref SystemState state)
        {
            for (int i = 0; i < 10; i++)
            {
                if (SystemAPI.HasComponent<VATAnimationState>(current))
                    return current;
                if (!SystemAPI.HasComponent<Parent>(current))
                    return Entity.Null;
                current = SystemAPI.GetComponent<Parent>(current).Value;
            }
            return Entity.Null;
        }
    }
}
