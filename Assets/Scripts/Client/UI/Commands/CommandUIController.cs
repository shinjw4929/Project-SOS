using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Shared;
using Client;

/// <summary>
/// 커맨드 UI 컨트롤러
/// - Build 버튼, 건물 선택 버튼 등 RTS 커맨드 UI 관리
/// - CurrentSelection을 활용하여 건설 가능 여부 판단
/// </summary>
public class CommandUIController : MonoBehaviour
{
    [Header("Main Panel")]
    [SerializeField] private GameObject mainCommandPanel;
    [SerializeField] private Button buildButton;

    [Header("Sub Panel (Build Menu)")]
    [SerializeField] private GameObject buildMenuPanel;
    [SerializeField] private Button btnBuildWall;
    [SerializeField] private Button btnBuildBarracks;

    private World _clientWorld;
    private EntityManager _em;
    private EntityQuery _currentSelectionQuery;
    private EntityQuery _userStateQuery;
    private EntityQuery _selectionStateQuery;
    private EntityQuery _previewStateQuery;
    private EntityQuery _refsQuery;

    private void Start()
    {
        if (buildButton) buildButton.onClick.AddListener(OnBuildButtonClicked);
        if (btnBuildWall) btnBuildWall.onClick.AddListener(() => OnStructureButtonClicked(isWall: true));
        if (btnBuildBarracks) btnBuildBarracks.onClick.AddListener(() => OnStructureButtonClicked(isWall: false));

        HideAllPanels();
    }

    private void Update()
    {
        if (!TryInitClientWorld()) return;
        if (_userStateQuery.IsEmptyIgnoreFilter) return;

        var userState = _userStateQuery.GetSingleton<UserState>();

        switch (userState.CurrentState)
        {
            case UserContext.Command:
                // 건설 가능 유닛이 선택된 경우에만 Build 버튼 표시
                if (CanShowBuildButton())
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
                if (mainCommandPanel) mainCommandPanel.SetActive(false);
                if (buildMenuPanel) buildMenuPanel.SetActive(true);
                break;

            case UserContext.Construction:
                HideAllPanels();
                break;

            default:
                HideAllPanels();
                break;
        }
    }

    private void OnBuildButtonClicked()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;
        if (_selectionStateQuery.IsEmptyIgnoreFilter) return;

        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        ref var selectionState = ref _selectionStateQuery.GetSingletonRW<SelectionState>().ValueRW;

        // 선택 상태 초기화 후 건설 메뉴 진입
        selectionState.Phase = SelectionPhase.Idle;
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

    /// <summary>
    /// Build 버튼을 표시할 수 있는지 확인
    /// - 건설 가능 유닛(HasBuilder)이 선택되어 있고, 내 소유여야 함
    /// </summary>
    private bool CanShowBuildButton()
    {
        if (_currentSelectionQuery.IsEmptyIgnoreFilter) return false;

        var selection = _currentSelectionQuery.GetSingleton<CurrentSelection>();

        // 선택 없거나, 내 소유가 아니면 false
        if (selection.SelectedCount == 0 || !selection.IsOwnedSelection) return false;

        // 건설 가능 유닛이 있어야 함
        return selection.HasBuilder;
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

                _currentSelectionQuery = _em.CreateEntityQuery(typeof(CurrentSelection));
                _userStateQuery = _em.CreateEntityQuery(typeof(UserState));
                _selectionStateQuery = _em.CreateEntityQuery(typeof(SelectionState));
                _previewStateQuery = _em.CreateEntityQuery(typeof(StructurePreviewState));
                _refsQuery = _em.CreateEntityQuery(typeof(StructureEntitiesReferences));

                return true;
            }
        }
        return false;
    }
}