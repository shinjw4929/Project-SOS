using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// [Client 전용] 게임에 필요한 싱글톤 엔티티(UserState, Selection 등)를 일괄 생성합니다.
    /// </summary>
    
    //초기화 단계에서 가장 먼저 실행되도록 설정
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct ClientBootstrapSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 1회만 실행하고 시스템 꺼버림 (성능 낭비 방지)
            state.Enabled = false;
            
            var entityManager = state.EntityManager;
            
            // [싱글톤 생성]
            if (!SystemAPI.HasSingleton<UserState>())
            {
                var singletonUserState = entityManager.CreateEntity();
                entityManager.AddComponentData(singletonUserState, new UserState { CurrentState = UserContext.Command });
                entityManager.SetName(singletonUserState, "Singleton_UserState");
            }
            
            if (!SystemAPI.HasSingleton<StructurePreviewState>())
            {
                var singletonStructurePreviewState = entityManager.CreateEntity();
                state.EntityManager.AddComponentData(singletonStructurePreviewState, new StructurePreviewState());
                entityManager.SetName(singletonStructurePreviewState, "Singleton_StructurePreviewState");
            }

            if (!SystemAPI.HasSingleton<SelectionState>())
            {
                var singletonSelectionState = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(singletonSelectionState, new SelectionState
                {
                    Phase = SelectionPhase.Idle,
                    StartScreenPos = float2.zero,
                    CurrentScreenPos = float2.zero
                });
                entityManager.SetName(singletonSelectionState, "Singleton_SelectionState");
            }

            if (!SystemAPI.HasSingleton<CurrentSelectionState>())
            {
                var singletonCurrentSelectionState = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(singletonCurrentSelectionState, new CurrentSelectionState());
                entityManager.SetName(singletonCurrentSelectionState, "Singleton_CurrentSelectionState");
            }
        }
    }
}

