using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Shared; // UnitTag, WallTag, BarracksTag, UserState
using Client; // StructurePreviewState

public class CommandUIController : MonoBehaviour
{
    [Header("Main Panel")]
    [SerializeField] private GameObject mainCommandPanel; // Build 버튼이 있는 패널
    [SerializeField] private Button buildButton;

    [Header("Sub Panel (Build Menu)")]
    [SerializeField] private GameObject buildMenuPanel;   // Wall, Barracks 버튼이 있는 패널
    [SerializeField] private Button btnBuildWall;
    [SerializeField] private Button btnBuildBarracks;

    private World _clientWorld;
    private EntityManager _em;
    private EntityQuery _currentSelectionQuery;
    private EntityQuery _userStateQuery;
    private EntityQuery _selectionStateQuery;
    private EntityQuery _selectionBoxQuery;
    private EntityQuery _previewStateQuery;
    private EntityQuery _refsQuery;

    private void Start()
    {
        // 버튼 이벤트 연결
        if (buildButton) buildButton.onClick.AddListener(OnBuildButtonClicked);
        if (btnBuildWall) btnBuildWall.onClick.AddListener(() => OnStructureButtonClicked(isWall: true));
        if (btnBuildBarracks) btnBuildBarracks.onClick.AddListener(() => OnStructureButtonClicked(isWall: false));

        // 초기 패널 상태
        HideAllPanels();
    }

    private void Update()
    {
        if (!TryInitClientWorld()) return;
        if (_userStateQuery.IsEmptyIgnoreFilter) return;
        
        // 현재 UserState 가져오기
        var userState = _userStateQuery.GetSingleton<UserState>();
        
        // BuildMenu나 Construction 상태에서는 유닛 선택과 관계없이 UI 표시
        switch (userState.CurrentState)
        {
            case UserContext.Command:
                // 기본 상태: 내 유닛이 선택된 경우에만 Build 버튼 표시
                if (IsMyUnitSelected())
                {
                    if (mainCommandPanel) mainCommandPanel.SetActive(true);
                    if (buildMenuPanel) buildMenuPanel.SetActive(false);
                }
                else
                {
                    HideAllPanels();
                }
                break;

            case UserContext.BuildMenu:
                // 건설 메뉴 상태: 건물 선택 버튼들 표시 (Q 키로 진입)
                if (mainCommandPanel) mainCommandPanel.SetActive(false);
                if (buildMenuPanel) buildMenuPanel.SetActive(true);
                break;

            case UserContext.Construction:
                // 건설 배치 중: UI 숨김
                HideAllPanels();
                break;

            default:
                HideAllPanels();
                break;
        }
    }

    // [Action] Build 버튼 클릭 -> 메뉴 진입
    private void OnBuildButtonClicked()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;
        if (_selectionStateQuery.IsEmptyIgnoreFilter) return;
        if (_selectionBoxQuery.IsEmptyIgnoreFilter) return;
        
        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        ref var selectionState = ref _selectionStateQuery.GetSingletonRW<SelectionState>().ValueRW;
        ref var selectionBox = ref _selectionBoxQuery.GetSingletonRW<SelectionBox>().ValueRW;

        selectionState.Mode = SelectionMode.Idle;
        selectionBox.IsDragging = false;
        userState.CurrentState = UserContext.BuildMenu;
    }

    // [Action] 건물 버튼 클릭 -> 건설 모드 진입 & 프리팹 설정
    private void OnStructureButtonClicked(bool isWall)
    {
        if (_userStateQuery.IsEmptyIgnoreFilter || _previewStateQuery.IsEmptyIgnoreFilter || _refsQuery.IsEmptyIgnoreFilter) return;

        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        ref var previewState = ref _previewStateQuery.GetSingletonRW<StructurePreviewState>().ValueRW;

        // 1. 프리팹 찾기 (System과 동일한 로직을 UI에서도 수행)
        var (foundPrefab, foundIndex) = FindPrefabByTag(isWall);

        if (foundPrefab != Entity.Null)
        {
            // 2. 상태 변경 및 프리팹 할당
            userState.CurrentState = UserContext.Construction;
            previewState.SelectedPrefab = foundPrefab;
            previewState.SelectedPrefabIndex = foundIndex;

            // 3. GridPosition 무효화 (이전 위치에서 깜빡이는 문제 방지)
            previewState.GridPosition = new int2(-1000, -1000);
            previewState.IsValidPlacement = false;
        }
        else
        {
            Debug.LogError("Prefab not found in UI Controller!");
        }
    }

    // [Helper] ECS Buffer를 순회하며 태그로 프리팹 찾기 (인덱스도 함께 반환)
    private (Entity prefab, int index) FindPrefabByTag(bool isWall)
    {
        var refsEntity = _refsQuery.GetSingletonEntity();
        var buffer = _em.GetBuffer<StructurePrefabElement>(refsEntity);

        for (int i = 0; i < buffer.Length; i++)
        {
            Entity prefab = buffer[i].PrefabEntity;
            if (prefab == Entity.Null || !_em.Exists(prefab)) continue;

            if (isWall)
            {
                if (_em.HasComponent<WallTag>(prefab)) return (prefab, i);
            }
            else // Barracks
            {
                if (_em.HasComponent<BarracksTag>(prefab)) return (prefab, i);
            }
        }
        return (Entity.Null, -1);
    }

    // [Helper] 선택된 유닛이 유효하고 내 것인지 확인
    private bool IsMyUnitSelected()
    {
        if (_currentSelectionQuery.IsEmptyIgnoreFilter) return false;
        var selection = _currentSelectionQuery.GetSingleton<CurrentSelectedUnit>();

        if (!selection.HasSelection || !_em.Exists(selection.SelectedEntity)) return false;

        // UnitTag가 있어야 함 (건물 선택 시에는 건설 UI 안 나옴)
        if (!_em.HasComponent<UnitTag>(selection.SelectedEntity)) return false;

        // (추가 가능) 내 팀 유닛인지 확인하는 로직 (GhostOwnerIsLocal 등)
        
        return true;
    }

    private void HideAllPanels()
    {
        if (mainCommandPanel) mainCommandPanel.SetActive(false);
        if (buildMenuPanel) buildMenuPanel.SetActive(false);
    }

    private bool TryInitClientWorld()
    {
        if (_clientWorld != null && _clientWorld.IsCreated) return true;

        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                _clientWorld = world;
                _em = world.EntityManager;
                
                // 쿼리 캐싱
                _currentSelectionQuery = _em.CreateEntityQuery(typeof(CurrentSelectedUnit));
                _userStateQuery = _em.CreateEntityQuery(typeof(UserState));
                _selectionStateQuery = _em.CreateEntityQuery(typeof(SelectionState));
                _selectionBoxQuery = _em.CreateEntityQuery(typeof(SelectionBox));
                _previewStateQuery = _em.CreateEntityQuery(typeof(StructurePreviewState));
                _refsQuery = _em.CreateEntityQuery(typeof(StructureEntitiesReferences));

                return true;
            }
        }
        return false;
    }
}