using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Shared;
using Client;

/// <summary>
/// 건물 명령 UI 컨트롤러
/// - Command 상태 + 건물 선택: Function 버튼 + 생산 진행도 (배럭)
/// - StructureMenu 상태: 건물 타입별 명령 버튼 + 생산 진행도 (배럭)
/// </summary>
public class StructureCommandUIController : MonoBehaviour
{
    [Header("Structure Command Panel")]
    [SerializeField] private GameObject structureCommandPanel;
    [SerializeField] private Text structureNameText;

    [Header("Function Button (Command 상태에서 건물 선택 시)")]
    [SerializeField] private GameObject functionButtonPanel;
    [SerializeField] private Button functionButton;

    [Header("Wall Commands (StructureMenu 상태)")]
    [SerializeField] private GameObject wallCommandsPanel;
    [SerializeField] private Button selfDestructButton;

    [Header("Barracks Commands (StructureMenu 상태)")]
    [SerializeField] private GameObject barracksCommandsPanel;
    [SerializeField] private Button produceSoldierButton;
    [SerializeField] private Button produceUnit2Button;

    [Header("Production Progress (배럭 선택 시 항상 표시)")]
    [SerializeField] private GameObject productionProgressPanel;
    [SerializeField] private Slider productionProgressBar;
    [SerializeField] private Text productionProgressText;

    private World _clientWorld;
    private EntityManager _em;
    private EntityQuery _CurrentSelectionStateQuery;
    private EntityQuery _userStateQuery;

    private void Start()
    {
        if (functionButton) functionButton.onClick.AddListener(OnFunctionButtonClicked);
        if (selfDestructButton) selfDestructButton.onClick.AddListener(OnSelfDestructClicked);
        if (produceSoldierButton) produceSoldierButton.onClick.AddListener(() => OnProduceUnitClicked(0));
        if (produceUnit2Button) produceUnit2Button.onClick.AddListener(() => OnProduceUnitClicked(1));

        HideAllPanels();
    }

    private void Update()
    {
        if (!TryInitClientWorld()) return;
        if (_userStateQuery.IsEmptyIgnoreFilter) return;
        if (_CurrentSelectionStateQuery.IsEmptyIgnoreFilter)
        {
            HideAllPanels();
            return;
        }

        var userState = _userStateQuery.GetSingleton<UserState>();
        var selection = _CurrentSelectionStateQuery.GetSingleton<CurrentSelectionState>();

        // 건물이 아니거나 내 소유가 아니면 숨김
        if (selection.Category != SelectionCategory.Structure || !selection.IsOwnedSelection)
        {
            HideAllPanels();
            return;
        }

        var primaryEntity = selection.PrimaryEntity;
        if (primaryEntity == Entity.Null || !_em.Exists(primaryEntity))
        {
            HideAllPanels();
            return;
        }

        // Command 상태: Function 버튼 + 생산 진행도 (배럭인 경우)
        if (userState.CurrentState == UserContext.Command)
        {
            ShowCommandStateUI(primaryEntity);
        }
        // StructureMenu 상태: 건물별 명령 버튼 + 생산 진행도 (배럭인 경우)
        else if (userState.CurrentState == UserContext.StructureActionMenu)
        {
            ShowStructureMenuUI(primaryEntity);
        }
        else
        {
            HideAllPanels();
        }
    }

    private void OnValidate()
    {
        // Inspector에서 연결 상태 확인용
        if (functionButtonPanel == null) Debug.LogWarning("[StructureCommandUI] functionButtonPanel이 연결되지 않았습니다!");
        if (wallCommandsPanel == null) Debug.LogWarning("[StructureCommandUI] wallCommandsPanel이 연결되지 않았습니다!");
        if (barracksCommandsPanel == null) Debug.LogWarning("[StructureCommandUI] barracksCommandsPanel이 연결되지 않았습니다!");
    }

    /// <summary>
    /// Command 상태: Function 버튼 + 생산 진행도 (배럭인 경우)
    /// </summary>
    private void ShowCommandStateUI(Entity primaryEntity)
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(true);

        // Function 버튼 표시
        if (functionButtonPanel) functionButtonPanel.SetActive(true);

        // 명령 패널은 숨김
        if (wallCommandsPanel) wallCommandsPanel.SetActive(false);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(false);

        // 건물 이름 표시
        if (_em.HasComponent<WallTag>(primaryEntity))
        {
            if (structureNameText) structureNameText.text = "Wall";
            // 자폭 진행 중이면 슬라이더 표시
            UpdateSelfDestructProgress(primaryEntity);
        }
        else if (_em.HasComponent<ProductionFacilityTag>(primaryEntity))
        {
            if (structureNameText) structureNameText.text = "Barracks";
            // 배럭이면 생산 진행도 표시
            UpdateProductionProgress(primaryEntity);
        }
        else
        {
            if (structureNameText) structureNameText.text = "Structure";
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
        }
    }

    /// <summary>
    /// StructureMenu 상태: 건물별 명령 버튼 + 생산 진행도 (배럭인 경우)
    /// </summary>
    private void ShowStructureMenuUI(Entity primaryEntity)
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(true);

        // Function 버튼 숨김
        if (functionButtonPanel) functionButtonPanel.SetActive(false);

        // 벽인 경우
        if (_em.HasComponent<WallTag>(primaryEntity))
        {
            ShowWallCommands();
        }
        // 생산 시설인 경우
        else if (_em.HasComponent<ProductionFacilityTag>(primaryEntity))
        {
            ShowBarracksCommands(primaryEntity);
        }
        else
        {
            HideAllPanels();
        }
    }

    private void OnFunctionButtonClicked()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;

        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        userState.CurrentState = UserContext.StructureActionMenu;

    }

    private void ShowWallCommands()
    {
        if (structureNameText) structureNameText.text = "Wall";
        if (functionButtonPanel) functionButtonPanel.SetActive(false);
        if (wallCommandsPanel) wallCommandsPanel.SetActive(true);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(false);

        // 자폭 진행 중이면 슬라이더 표시 + 버튼 상태 업데이트
        var selection = _CurrentSelectionStateQuery.GetSingleton<CurrentSelectionState>();
        var wallEntity = selection.PrimaryEntity;
        UpdateSelfDestructProgress(wallEntity, updateButton: true);
    }

    /// <summary>
    /// 벽 자폭 진행도 UI 업데이트
    /// </summary>
    /// <param name="updateButton">StructureMenu에서 호출 시 버튼 상태도 업데이트</param>
    private void UpdateSelfDestructProgress(Entity wallEntity, bool updateButton = false)
    {
        // SelfDestructTag 확인
        if (!_em.HasComponent<SelfDestructTag>(wallEntity))
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            if (updateButton && selfDestructButton) selfDestructButton.interactable = true;
            return;
        }

        var selfDestruct = _em.GetComponentData<SelfDestructTag>(wallEntity);

        // RemainingTime < 0이면 자폭 대기 아님
        if (selfDestruct.RemainingTime < 0)
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            if (updateButton && selfDestructButton) selfDestructButton.interactable = true;
            return;
        }

        // ExplosionData에서 원래 Delay 가져오기
        if (!_em.HasComponent<ExplosionData>(wallEntity))
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            return;
        }

        var explosionData = _em.GetComponentData<ExplosionData>(wallEntity);
        float totalDelay = explosionData.Delay;

        if (totalDelay <= 0)
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            return;
        }

        if (productionProgressPanel) productionProgressPanel.SetActive(true);

        // 진행도: (Delay - RemainingTime) / Delay
        float progress = (totalDelay - selfDestruct.RemainingTime) / totalDelay;
        progress = UnityEngine.Mathf.Clamp01(progress);

        if (productionProgressBar) productionProgressBar.value = progress;
        if (productionProgressText) productionProgressText.text = $"폭발 {selfDestruct.RemainingTime:F1}s";

        // 자폭 버튼 비활성화 (이미 자폭 중)
        if (updateButton && selfDestructButton) selfDestructButton.interactable = false;
    }

    private void ShowBarracksCommands(Entity barracksEntity)
    {
        if (structureNameText) structureNameText.text = "Barracks";
        if (functionButtonPanel) functionButtonPanel.SetActive(false);
        if (wallCommandsPanel) wallCommandsPanel.SetActive(false);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(true);

        // 생산 진행도 + 버튼 활성화 상태 업데이트
        UpdateProductionProgress(barracksEntity, updateButtons: true);
    }

    /// <summary>
    /// 생산 진행도 UI 업데이트
    /// </summary>
    private void UpdateProductionProgress(Entity barracksEntity, bool updateButtons = false)
    {
        if (!_em.HasComponent<ProductionQueue>(barracksEntity))
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            return;
        }

        var queue = _em.GetComponentData<ProductionQueue>(barracksEntity);

        if (queue.IsActive && queue.Duration > 0)
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(true);

            float progress = queue.Progress / queue.Duration;
            if (productionProgressBar) productionProgressBar.value = progress;
            if (productionProgressText) productionProgressText.text = $"{(int)(progress * 100)}%";

            // StructureMenu에서만 버튼 상태 업데이트
            if (updateButtons)
            {
                if (produceSoldierButton) produceSoldierButton.interactable = false;
                if (produceUnit2Button) produceUnit2Button.interactable = false;
            }
        }
        else
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);

            if (updateButtons)
            {
                if (produceSoldierButton) produceSoldierButton.interactable = true;
                if (produceUnit2Button) produceUnit2Button.interactable = true;
            }
        }
    }

    private void OnSelfDestructClicked()
    {
        if (_CurrentSelectionStateQuery.IsEmptyIgnoreFilter) return;

        var selection = _CurrentSelectionStateQuery.GetSingleton<CurrentSelectionState>();
        if (selection.PrimaryEntity == Entity.Null) return;

        var primaryEntity = selection.PrimaryEntity;
        if (!_em.HasComponent<WallTag>(primaryEntity)) return;
        if (!_em.HasComponent<GhostInstance>(primaryEntity)) return;

        var ghostInstance = _em.GetComponentData<GhostInstance>(primaryEntity);

        // RPC 전송
        var rpcEntity = _em.CreateEntity();
        _em.AddComponentData(rpcEntity, new SelfDestructRequestRpc
        {
            TargetGhostId = ghostInstance.ghostId
        });
        _em.AddComponent<SendRpcCommandRequest>(rpcEntity);

        // Command 상태로 복귀
        ReturnToCommandState();
    }

    private void OnProduceUnitClicked(int unitIndex)
    {
        if (_CurrentSelectionStateQuery.IsEmptyIgnoreFilter) return;

        var selection = _CurrentSelectionStateQuery.GetSingleton<CurrentSelectionState>();
        if (selection.PrimaryEntity == Entity.Null) return;

        var primaryEntity = selection.PrimaryEntity;
        if (!_em.HasComponent<ProductionFacilityTag>(primaryEntity)) return;
        if (!_em.HasComponent<GhostInstance>(primaryEntity)) return;

        // 버퍼 확인
        if (!_em.HasBuffer<UnitCatalogElement>(primaryEntity)) return;
        var unitBuffer = _em.GetBuffer<UnitCatalogElement>(primaryEntity);
        if (unitIndex >= unitBuffer.Length) return;

        var ghostInstance = _em.GetComponentData<GhostInstance>(primaryEntity);

        // RPC 전송
        var rpcEntity = _em.CreateEntity();
        _em.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
        {
            StructureGhostId = ghostInstance.ghostId,
            UnitIndex = unitIndex
        });
        _em.AddComponent<SendRpcCommandRequest>(rpcEntity);

        // Command 상태로 복귀
        ReturnToCommandState();
    }

    private void ReturnToCommandState()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;

        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        userState.CurrentState = UserContext.Command;
    }

    private void HideAllPanels()
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(false);
        if (functionButtonPanel) functionButtonPanel.SetActive(false);
        if (wallCommandsPanel) wallCommandsPanel.SetActive(false);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(false);
        if (productionProgressPanel) productionProgressPanel.SetActive(false);
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

                _CurrentSelectionStateQuery = _em.CreateEntityQuery(typeof(CurrentSelectionState));
                _userStateQuery = _em.CreateEntityQuery(typeof(UserState));

                return true;
            }
        }
        return false;
    }
}
