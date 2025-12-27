using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Unity.Collections;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructureSelectInputSystem : ISystem
    {
        // [Entity -> int] 검색표
        private NativeParallelHashMap<Entity, int> _prefabIndexMap;
        
        public void OnCreate(ref SystemState state)
        {
            // 맵 초기화 (64개 맵핑)
            _prefabIndexMap = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<CurrentSelection>();

            if (!SystemAPI.HasSingleton<StructurePreviewState>())
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, new StructurePreviewState());
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_prefabIndexMap.IsCreated) _prefabIndexMap.Dispose();
        }
        
        public void OnUpdate(ref SystemState state)
        {
            // 1. 해시맵 캐싱 (데이터가 로드되었고 맵이 비었을 때)
            if (_prefabIndexMap.IsEmpty && SystemAPI.HasSingleton<StructureCatalog>())
            {
                BuildIndexMap(ref state);
            }
            
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;
            
            // 선택된 유닛 정보 가져오기
            var selection = SystemAPI.GetSingleton<CurrentSelection>();
            // -----------------------------------------------------------------
            // 상태별 입력 처리
            // -----------------------------------------------------------------
            
            if (userState.CurrentState == UserContext.Command)
            {
                // 1. 기본 상태일 때 (Q를 눌러 건설 메뉴 진입)
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    // 건설 유닛인지, 1개 유닛만 선택했는지, 내가 소유한 유닛인지 확인
                    if (!selection.HasBuilder || selection.SelectedCount != 1 || !selection.IsOwnedSelection) return;
                    
                    // 건설 메뉴 상태로 진입 (아직 건물 선택 안 함)
                    if (state.EntityManager.HasBuffer<AvailableStructure>(selection.PrimaryEntity))
                    {
                        userState.CurrentState = UserContext.BuildMenu;
                    }
                }
            }
            else if (userState.CurrentState == UserContext.BuildMenu)
            {
                // 2. 건설 메뉴 상태일 때 (Q/W로 건물 선택)
                // Q = 리스트의 0번, W = 리스트의 1번
                int targetLocalIndex = -1;
                
                if (keyboard.qKey.wasPressedThisFrame) targetLocalIndex = 0;
                else if (keyboard.wKey.wasPressedThisFrame) targetLocalIndex = 1;
                //else if (keyboard.eKey.wasPressedThisFrame) targetLocalIndex = 2; // 확장 시
                
                if (targetLocalIndex != -1)
                {
                    // [변경] 태그 검색 대신 -> 유닛의 버퍼 조회 함수 호출
                    TrySelectStructureFromUnit(
                        ref userState, 
                        ref previewState, 
                        state.EntityManager, 
                        selection.PrimaryEntity, 
                        targetLocalIndex
                    );
                }
                else if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    // ESC : 메뉴 닫기 (Command로 복귀)
                    userState.CurrentState = UserContext.Command;
                }
            }
            else if (userState.CurrentState == UserContext.Construction)
            {
                // 3. 건설 중일 때 (ESC로 취소)
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    userState.CurrentState = UserContext.Command;
                    previewState.SelectedPrefab = Entity.Null;
                }
            }
        }
        
        /// <summary>
        /// 선택된 유닛의 버퍼에서 N번째 건물을 가져와 전역 인덱스로 변환
        /// </summary>
        private void TrySelectStructureFromUnit(
            ref UserState userState, 
            ref StructurePreviewState previewState, 
            EntityManager entityManager, 
            Entity builderEntity, 
            int localIndex)
        {
            // 1. 유닛의 건설 목록 버퍼 가져오기
            var structureBuffer = entityManager.GetBuffer<AvailableStructure>(builderEntity);

            // 2. 인덱스 유효성 검사
            if (localIndex < 0 || localIndex >= structureBuffer.Length)
            {
                // 삑 소리 재생 or UI 피드백 (건설 불가)
                return;
            }

            // 3. 짓고자 하는 프리팹 확인
            Entity targetPrefab = structureBuffer[localIndex].PrefabEntity;

            // 4. 프리팹이 전역 카탈로그의 몇 번째인지 해시맵 조회 (RPC용)
            if (_prefabIndexMap.TryGetValue(targetPrefab, out int globalIndex))
            {
                // 성공! 상태 업데이트
                userState.CurrentState = UserContext.Construction;
                
                previewState.SelectedPrefab = targetPrefab;
                previewState.SelectedPrefabIndex = globalIndex; // 서버로 보낼 값
                
                previewState.GridPosition = new int2(-1000, -1000);
                previewState.IsValidPlacement = false;
            }
            else
            {
                UnityEngine.Debug.LogWarning($"Prefab {targetPrefab} not found in Global Catalog Map!");
            }
        }
        
        private void BuildIndexMap(ref SystemState state)
        {
            var catalogEntity = SystemAPI.GetSingletonEntity<StructureCatalog>();
            var buffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity);

            _prefabIndexMap.Clear();
            for (int i = 0; i < buffer.Length; i++)
            {
                if(buffer[i].PrefabEntity != Entity.Null)
                {
                    // 같은 프리팹이 중복되면 덮어씌워지지만, 카탈로그는 고유하다고 가정
                    _prefabIndexMap.TryAdd(buffer[i].PrefabEntity, i);
                }
            }
        }
    }
}