using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct TeamColorSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Team>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Phase 1: 새 메시 엔티티에 TeamColorTarget + URPMaterialPropertyBaseColor 부착
            InitializeNewMeshEntities(ref state);

            // Phase 2: 팀 색상 갱신
            var job = new TeamColorJob
            {
                TeamLookup = SystemAPI.GetComponentLookup<Team>(true)
            };
            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        private void InitializeNewMeshEntities(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool hasNew = false;

            foreach (var (parent, entity) in
                SystemAPI.Query<RefRO<Parent>>()
                    .WithAll<MaterialMeshInfo>()
                    .WithNone<TeamColorTarget>()
                    .WithEntityAccess())
            {
                Entity rootEntity = FindTeamAncestor(parent.ValueRO.Value, ref state);

                // TeamColorTarget 부착 (Entity.Null이면 팀 미소속 → 색상 갱신 스킵)
                ecb.AddComponent(entity, new TeamColorTarget { RootEntity = rootEntity });

                if (rootEntity != Entity.Null)
                {
                    ecb.AddComponent(entity, new URPMaterialPropertyBaseColor
                    {
                        Value = new float4(1, 1, 1, 1)
                    });
                }

                hasNew = true;
            }

            if (hasNew)
            {
                ecb.Playback(state.EntityManager);
            }
            ecb.Dispose();
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

        void Execute(in TeamColorTarget target, ref URPMaterialPropertyBaseColor color)
        {
            if (target.RootEntity == Entity.Null) return;
            if (!TeamLookup.TryGetComponent(target.RootEntity, out Team team)) return;

            color.Value = TeamColorPalette.GetTeamColor(team.teamId);
        }
    }
}
