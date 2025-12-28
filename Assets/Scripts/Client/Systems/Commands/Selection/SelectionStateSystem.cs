using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// 선택 상태 계산 시스템
    /// - Selected 엔티티 순회 → CurrentSelection 싱글톤 업데이트
    /// - UI 표시 및 건설모드 진입 조건 체크에 사용
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(EntitySelectionSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SelectionStateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<CurrentSelection>();
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var myNetworkId)) return;
            int myTeamId = myNetworkId.Value;

            ref var currentSelection = ref SystemAPI.GetSingletonRW<CurrentSelection>().ValueRW;

            // 초기화
            currentSelection.PrimaryEntity = Entity.Null;
            currentSelection.SelectedCount = 0;
            currentSelection.Category = SelectionCategory.None;
            currentSelection.HasBuilder = false;
            currentSelection.IsOwnedSelection = true;

            bool hasUnits = false;
            bool hasStructures = false;
            bool isFirst = true;

            // 선택된 엔티티 순회
            foreach (var (selected, entity) in SystemAPI.Query<RefRO<Selected>>()
                .WithEntityAccess())
            {
                // Selected 컴포넌트가 비활성화된 경우 스킵
                if (!state.EntityManager.IsComponentEnabled<Selected>(entity)) continue;

                currentSelection.SelectedCount++;

                // 첫 번째 엔티티를 대표로 저장
                if (isFirst)
                {
                    currentSelection.PrimaryEntity = entity;
                    isFirst = false;
                }

                // 유닛/건물 구분
                if (state.EntityManager.HasComponent<UnitTag>(entity))
                {
                    hasUnits = true;
                    // 건설 가능 유닛인지 체크 (BuilderTag가 있다면)                                                              
                    if (state.EntityManager.HasComponent<BuilderTag>(entity))                                                     
                    {                                                                                                             
                        currentSelection.HasBuilder = true;                                                                       
                    }  
                }

                if (state.EntityManager.HasComponent<StructureTag>(entity))
                {
                    hasStructures = true;
                }

                // 내 소유인지 체크
                if (state.EntityManager.HasComponent<Team>(entity))
                {
                    var team = state.EntityManager.GetComponentData<Team>(entity);
                    if (team.teamId != myTeamId)
                    {
                        currentSelection.IsOwnedSelection = false;
                    }
                }
            }

            // 카테고리 결정
            if (hasUnits && hasStructures)
            {
                currentSelection.Category = SelectionCategory.Mixed;
            }
            else if (hasUnits)
            {
                currentSelection.Category = SelectionCategory.Units;
            }
            else if (hasStructures)
            {
                currentSelection.Category = SelectionCategory.Structure;
            }
            else
            {
                currentSelection.Category = SelectionCategory.None;
                currentSelection.IsOwnedSelection = false;
            }
        }
    }
}
