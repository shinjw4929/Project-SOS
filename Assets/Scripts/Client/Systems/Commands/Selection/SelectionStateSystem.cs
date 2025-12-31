using Unity.Burst;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using Shared;
using Client;

namespace Client
{
    /// <summary>
    /// 선택 상태 계산 시스템
    /// - Selected 엔티티 순회 → CurrentSelectionState 싱글톤 업데이트
    /// - UI 표시 및 건설모드 진입 조건 체크에 사용
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(EntitySelectionSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SelectionStateSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<CurrentSelectionState>();
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var myNetworkId)) return;
            int myTeamId = myNetworkId.Value;

            ref var CurrentSelectionState = ref SystemAPI.GetSingletonRW<CurrentSelectionState>().ValueRW;

            // 초기화
            CurrentSelectionState.PrimaryEntity = Entity.Null;
            CurrentSelectionState.SelectedCount = 0;
            CurrentSelectionState.Category = SelectionCategory.None;
            CurrentSelectionState.IsOwnedSelection = true;

            bool hasUnits = false;
            bool hasStructures = false;
            bool isFirst = true;

            // 선택된 엔티티 순회
            foreach (var (selected, entity) in SystemAPI.Query<RefRO<Selected>>()
                .WithEntityAccess())
            {
                // Selected 컴포넌트가 비활성화된 경우 스킵
                if (!state.EntityManager.IsComponentEnabled<Selected>(entity)) continue;

                CurrentSelectionState.SelectedCount++;

                // 첫 번째 엔티티를 대표로 저장
                if (isFirst)
                {
                    CurrentSelectionState.PrimaryEntity = entity;
                    isFirst = false;
                }

                // 유닛/건물 구분
                if (state.EntityManager.HasComponent<UnitTag>(entity))
                {
                    hasUnits = true;
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
                        CurrentSelectionState.IsOwnedSelection = false;
                    }
                }
            }

            // 카테고리 결정
            if (hasUnits && hasStructures)
            {
                throw new System.Exception("예상 하지 못한 오류 발생: 유닛과 구조체가 동시에 선택될 수 없습니다.");
            }
            else if (hasUnits)
            {
                CurrentSelectionState.Category = SelectionCategory.Units;
            }
            else if (hasStructures)
            {
                CurrentSelectionState.Category = SelectionCategory.Structure;
            }
            else
            {
                CurrentSelectionState.Category = SelectionCategory.None;
                CurrentSelectionState.IsOwnedSelection = false;
            }
        }
    }
}
