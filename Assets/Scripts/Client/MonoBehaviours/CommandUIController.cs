using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Shared;
using Client;

public class CommandUIController : MonoBehaviour
{
    [SerializeField] private GameObject panelContent;  // 자식 GameObject를 참조 (자기 자신이 아님)
    [SerializeField] private Button buildButton;

    private World _clientWorld;
    private EntityManager _clientEntityManager;
    private EntityQuery _currentSelectionQuery;
    private EntityQuery _buildStateQuery;

    private void Start()
    {
        if (buildButton != null)
            buildButton.onClick.AddListener(OnBuildButtonClicked);
    }

    private void Update()
    {
        if (!TryInitClientWorld())
            return;

        if (_currentSelectionQuery.IsEmptyIgnoreFilter)
        {
            HidePanel();
            return;
        }

        var currentSelection = _currentSelectionQuery.GetSingleton<CurrentSelectedUnit>();

        if (!currentSelection.hasSelection)
        {
            HidePanel();
            return;
        }

        if (!_clientEntityManager.Exists(currentSelection.selectedEntity))
        {
            HidePanel();
            return;
        }

        // Player 엔티티인지 확인 (건물은 명령 UI 없음)
        if (!_clientEntityManager.HasComponent<Player>(currentSelection.selectedEntity))
        {
            HidePanel();
            return;
        }

        ShowPanel();
    }

    private void OnBuildButtonClicked()
    {
        if (_buildStateQuery.IsEmptyIgnoreFilter) return;

        ref var buildState = ref _buildStateQuery.GetSingletonRW<PlayerBuildState>().ValueRW;
        buildState.isBuildMode = !buildState.isBuildMode;

        if (buildState.isBuildMode)
        {
            var previewQuery = _clientEntityManager.CreateEntityQuery(typeof(BuildingPreviewState));
            if (!previewQuery.IsEmptyIgnoreFilter)
            {
                ref var previewState = ref previewQuery.GetSingletonRW<BuildingPreviewState>().ValueRW;
                previewState.selectedType = BuildingTypeEnum.Wall;
            }
        }
    }

    private bool TryInitClientWorld()
    {
        if (_clientWorld != null && _clientWorld.IsCreated) return true;

        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                _clientWorld = world;
                _clientEntityManager = world.EntityManager;
                _currentSelectionQuery = _clientEntityManager.CreateEntityQuery(typeof(CurrentSelectedUnit));
                _buildStateQuery = _clientEntityManager.CreateEntityQuery(typeof(PlayerBuildState));
                return true;
            }
        }
        return false;
    }

    private void ShowPanel()
    {
        if (panelContent != null) panelContent.SetActive(true);
    }

    private void HidePanel()
    {
        if (panelContent != null) panelContent.SetActive(false);
    }
}
