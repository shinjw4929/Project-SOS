using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// [Client 전용] 게임에 필요한 싱글톤 엔티티를 일괄 생성합니다.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct ClientBootstrapSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // 이 시스템은 EntityManager를 직접 사용하므로 별도의 RequireForUpdate가 필요 없습니다.
        }
        
        public void OnUpdate(ref SystemState state)
        {
            // 1회 실행 후 즉시 비활성화
            state.Enabled = false;
            
            // 반복적인 EntityManager 접근을 줄이기 위해 로컬 변수 사용 (구조체 복사 비용 절감)
            var entityManager = state.EntityManager;

            // 1. UserState 생성
            if (!SystemAPI.HasSingleton<UserState>())
            {
                // 최적화: 엔티티 생성과 컴포넌트 추가를 아키타입으로 한 번에 처리
                var entity = entityManager.CreateEntity(typeof(UserState));
                
                // 데이터 값 설정
                SystemAPI.SetComponent(entity, new UserState { CurrentState = UserContext.Command });

#if UNITY_EDITOR
                // 최적화: 문자열 할당은 에디터에서만 수행 (빌드 시 오버헤드 제거)
                entityManager.SetName(entity, "Singleton_UserState");
#endif
            }

            // 2. StructurePreviewState 생성
            if (!SystemAPI.HasSingleton<StructurePreviewState>())
            {
                var entity = entityManager.CreateEntity(typeof(StructurePreviewState));
                // 기본 생성자 사용 시 SetComponent 생략 가능 (0으로 초기화됨)하거나 명시적 초기화
                SystemAPI.SetComponent(entity, new StructurePreviewState());

#if UNITY_EDITOR
                entityManager.SetName(entity, "Singleton_StructurePreviewState");
#endif
            }

            // 3. UserSelectionInputState 생성
            if (!SystemAPI.HasSingleton<UserSelectionInputState>())
            {
                var entity = entityManager.CreateEntity(typeof(UserSelectionInputState));
                SystemAPI.SetComponent(entity, new UserSelectionInputState
                {
                    Phase = SelectionPhase.Idle,
                    StartScreenPos = float2.zero,
                    CurrentScreenPos = float2.zero
                });

#if UNITY_EDITOR
                entityManager.SetName(entity, "Singleton_UserSelectionInputState");
#endif
            }

            // 4. CurrentSelectionState 생성
            if (!SystemAPI.HasSingleton<SelectedEntityInfoState>())
            {
                var entity = entityManager.CreateEntity(typeof(SelectedEntityInfoState));
                SystemAPI.SetComponent(entity, new SelectedEntityInfoState());

#if UNITY_EDITOR
                entityManager.SetName(entity, "Singleton_CurrentSelectionState");
#endif
            }
        }
    }
}
