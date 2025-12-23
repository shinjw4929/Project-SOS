using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UnitSelectionSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitInfoUISystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            Entity singletonEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(singletonEntity, new CurrentSelectedUnit
            {
                selectedEntity = Entity.Null,
                hasSelection = false
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            var currentSelectionRW = SystemAPI.GetSingletonRW<CurrentSelectedUnit>();

            Entity firstSelected = Entity.Null;
            bool found = false;

            foreach (var (selected, entity) in SystemAPI.Query<RefRO<Selected>>()
                .WithEntityAccess())
            {
                if (state.EntityManager.IsComponentEnabled<Selected>(entity))
                {
                    firstSelected = entity;
                    found = true;
                    break;
                }
            }

            currentSelectionRW.ValueRW.selectedEntity = firstSelected;
            currentSelectionRW.ValueRW.hasSelection = found;
        }
    }
}
