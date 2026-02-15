using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct TeamColorSystem : ISystem
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Team>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Phase 1: 새 메시 엔티티에 TeamColorTarget + OriginalBaseColor + URPMaterialPropertyBaseColor 부착
            InitializeNewMeshEntities(ref state);

            // Phase 2: 팀 색상 갱신 (원본 색상 * 팀 색상)
            var job = new TeamColorJob
            {
                TeamLookup = SystemAPI.GetComponentLookup<Team>(true)
            };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        private void InitializeNewMeshEntities(ref SystemState state)
        {
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool hasNew = false;

            foreach (var (parent, entity) in
                SystemAPI.Query<RefRO<Parent>>()
                    .WithAll<MaterialMeshInfo>()
                    .WithNone<TeamColorTarget>()
                    .WithEntityAccess())
            {
                Entity rootEntity = FindTeamAncestor(parent.ValueRO.Value, ref state);

                ecb.AddComponent(entity, new TeamColorTarget { RootEntity = rootEntity });

                if (rootEntity != Entity.Null)
                {
                    float4 originalColor = ReadOriginalBaseColor(em, entity);

                    ecb.AddComponent(entity, new OriginalBaseColor { Value = originalColor });
                    ecb.AddComponent(entity, new URPMaterialPropertyBaseColor
                    {
                        Value = originalColor
                    });
                }

                hasNew = true;
            }

            if (hasNew)
            {
                ecb.Playback(em);
            }
            ecb.Dispose();
        }

        private float4 ReadOriginalBaseColor(EntityManager em, Entity entity)
        {
            var renderMeshArray = em.GetSharedComponentManaged<RenderMeshArray>(entity);
            var materialMeshInfo = em.GetComponentData<MaterialMeshInfo>(entity);
            var material = renderMeshArray.GetMaterial(materialMeshInfo);

            if (material != null && material.HasProperty(BaseColorId))
            {
                var color = material.GetColor(BaseColorId);
                return new float4(color.r, color.g, color.b, color.a);
            }

            return new float4(1, 1, 1, 1);
        }

        private Entity FindTeamAncestor(Entity current, ref SystemState state)
        {
            for (int i = 0; i < 10; i++)
            {
                if (SystemAPI.HasComponent<Team>(current))
                    return current;
                if (!SystemAPI.HasComponent<Parent>(current))
                    return Entity.Null;
                current = SystemAPI.GetComponent<Parent>(current).Value;
            }
            return Entity.Null;
        }
    }

    [BurstCompile]
    public partial struct TeamColorJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<Team> TeamLookup;

        void Execute(in TeamColorTarget target, in OriginalBaseColor original, ref URPMaterialPropertyBaseColor color)
        {
            if (target.RootEntity == Entity.Null) return;
            if (!TeamLookup.TryGetComponent(target.RootEntity, out Team team)) return;

            color.Value = original.Value * TeamColorPalette.GetTeamColor(team.teamId);
        }
    }
}
