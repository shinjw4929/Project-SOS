using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Shared;
using Client;

public class StructureCommandUIController : MonoBehaviour
{
    [Header("Structure Command Panel")]
    [SerializeField] private GameObject structureCommandPanel;
    [SerializeField] private Text structureNameText;

    [Header("Function Button (Command 상태)")]
    [SerializeField] private GameObject functionButtonPanel;
    [SerializeField] private Button functionButton;

    [Header("Wall Commands (Menu 상태)")]
    [SerializeField] private GameObject wallCommandsPanel;
    [SerializeField] private Button selfDestructButton;

    [Header("Barracks Commands (Menu 상태)")]
    [SerializeField] private GameObject barracksCommandsPanel;
    [SerializeField] private Button produceSoldierButton; // Index 0
    [SerializeField] private Button produceUnit2Button;   // Index 1

    [Header("Production Progress")]
    [SerializeField] private GameObject productionProgressPanel;
    [SerializeField] private Slider productionProgressBar;
    [SerializeField] private Text productionProgressText;

    private World _clientWorld;
    private EntityManager _em;
    private EntityQuery _SelectedEntityInfoStateQuery;
    private EntityQuery _userStateQuery;
    private EntityQuery _unitCatalogQuery; // [추가] 전역 카탈로그 조회용

    private void Start()
    {
        if (functionButton) functionButton.onClick.AddListener(OnFunctionButtonClicked);
        if (selfDestructButton) selfDestructButton.onClick.AddListener(OnSelfDestructClicked);
        
        // 람다로 인덱스 전달
        if (produceSoldierButton) produceSoldierButton.onClick.AddListener(() => OnProduceUnitClicked(0));
        if (produceUnit2Button) produceUnit2Button.onClick.AddListener(() => OnProduceUnitClicked(1));

        HideAllPanels();
    }

    private void Update()
    {
        if (!TryInitClientWorld()) return;
        
        // 필수 싱글톤 확인
        if (_userStateQuery.IsEmptyIgnoreFilter || _SelectedEntityInfoStateQuery.IsEmptyIgnoreFilter)
        {
            HideAllPanels();
            return;
        }

        var userState = _userStateQuery.GetSingleton<UserState>();
        var selectedEntityInfoState = _SelectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();

        // 1. 선택 유효성 검사 (시스템과 동일)
        if (selectedEntityInfoState.Category != SelectionCategory.Structure || 
            !selectedEntityInfoState.IsOwnedSelection || 
            selectedEntityInfoState.PrimaryEntity == Entity.Null ||
            !_em.Exists(selectedEntityInfoState.PrimaryEntity))
        {
            HideAllPanels();
            return;
        }

        Entity primaryEntity = selectedEntityInfoState.PrimaryEntity;

        // 2. 상태별 UI 표시
        if (userState.CurrentState == UserContext.Command)
        {
            ShowCommandStateUI(primaryEntity);
        }
        else if (userState.CurrentState == UserContext.StructureActionMenu)
        {
            ShowStructureMenuUI(primaryEntity);
        }
        else
        {
            HideAllPanels();
        }
    }

    private void ShowCommandStateUI(Entity primaryEntity)
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(true);
        if (functionButtonPanel) functionButtonPanel.SetActive(true); // Function 버튼 보임

        if (wallCommandsPanel) wallCommandsPanel.SetActive(false);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(false);

        // 태그에 따른 정보 표시
        if (_em.HasComponent<WallTag>(primaryEntity))
        {
            if (structureNameText) structureNameText.text = "Wall";
            UpdateSelfDestructProgress(primaryEntity);
        }
        else if (_em.HasComponent<ProductionFacilityTag>(primaryEntity))
        {
            if (structureNameText) structureNameText.text = "Barracks";
            UpdateProductionProgress(primaryEntity);
        }
        else
        {
            if (structureNameText) structureNameText.text = "Structure";
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
        }
    }

    private void ShowStructureMenuUI(Entity primaryEntity)
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(true);
        if (functionButtonPanel) functionButtonPanel.SetActive(false); // Function 버튼 숨김 (메뉴 진입했으므로)

        if (_em.HasComponent<WallTag>(primaryEntity))
        {
            ShowWallCommands();
        }
        else if (_em.HasComponent<ProductionFacilityTag>(primaryEntity))
        {
            ShowBarracksCommands(primaryEntity);
        }
        else
        {
            // 태그가 없으면 메뉴를 표시할 수 없음
            HideAllPanels();
        }
    }

    // [Action] Function 버튼 클릭 (Q키와 동일 역할)
    private void OnFunctionButtonClicked()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;

        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        userState.CurrentState = UserContext.StructureActionMenu;
    }

    private void ShowWallCommands()
    {
        if (structureNameText) structureNameText.text = "Wall";
        if (wallCommandsPanel) wallCommandsPanel.SetActive(true);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(false);

        var selectedEntityInfoState = _SelectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();
        UpdateSelfDestructProgress(selectedEntityInfoState.PrimaryEntity, updateButton: true);
    }

    private void ShowBarracksCommands(Entity barracksEntity)
    {
        if (structureNameText) structureNameText.text = "Barracks";
        if (wallCommandsPanel) wallCommandsPanel.SetActive(false);
        if (barracksCommandsPanel) barracksCommandsPanel.SetActive(true);

        UpdateProductionProgress(barracksEntity, updateButtons: true);
    }

    // [Action] 유닛 생산 버튼 클릭 (Q/W키와 동일 역할)
    private void OnProduceUnitClicked(int localIndex)
    {
        if (_SelectedEntityInfoStateQuery.IsEmptyIgnoreFilter) return;
        var selectedEntityInfoState = _SelectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();
        var entity = selectedEntityInfoState.PrimaryEntity;

        // 유효성 검사
        if (entity == Entity.Null || !_em.Exists(entity)) return;
        if (!_em.HasComponent<ProductionFacilityTag>(entity)) return;
        if (!_em.HasBuffer<AvailableUnit>(entity)) return; // [수정] AvailableUnit 버퍼 확인

        // 1. 로컬 인덱스로 프리팹 찾기
        var unitBuffer = _em.GetBuffer<AvailableUnit>(entity);
        if (localIndex >= unitBuffer.Length) return;

        Entity targetPrefab = unitBuffer[localIndex].PrefabEntity;

        // 2. [핵심] 전역 카탈로그 인덱스 찾기 (InputSystem의 _prefabIndexMap 역할)
        int globalIndex = GetGlobalUnitIndex(targetPrefab);

        if (globalIndex == -1)
        {
            Debug.LogError($"[UI] Prefab {targetPrefab} not found in UnitCatalog!");
            return;
        }

        // 3. RPC 전송
        if (_em.HasComponent<GhostInstance>(entity))
        {
            var ghostInstance = _em.GetComponentData<GhostInstance>(entity);
            var rpcEntity = _em.CreateEntity();
            _em.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
            {
                StructureGhostId = ghostInstance.ghostId,
                UnitIndex = globalIndex // 반드시 Global Index여야 함
            });
            _em.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }

        // 4. Command 상태로 복귀 (InputSystem과 동일하게)
        // (연속 생산을 원하면 이 줄을 주석 처리)
        // ReturnToCommandState(); 
    }

    // [Action] 자폭 버튼 클릭 (R키와 동일 역할)
    private void OnSelfDestructClicked()
    {
        if (_SelectedEntityInfoStateQuery.IsEmptyIgnoreFilter) return;
        var selection = _SelectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();
        var entity = selection.PrimaryEntity;

        if (entity == Entity.Null || !_em.Exists(entity)) return;
        if (!_em.HasComponent<ExplosionData>(entity)) return;

        if (_em.HasComponent<GhostInstance>(entity))
        {
            var ghostInstance = _em.GetComponentData<GhostInstance>(entity);
            var rpcEntity = _em.CreateEntity();
            _em.AddComponentData(rpcEntity, new SelfDestructRequestRpc
            {
                TargetGhostId = ghostInstance.ghostId
            });
            _em.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }

        ReturnToCommandState();
    }

    // [Helper] 전역 유닛 인덱스 조회
    private int GetGlobalUnitIndex(Entity prefabEntity)
    {
        if (_unitCatalogQuery.IsEmptyIgnoreFilter) return -1;

        var catalogEntity = _unitCatalogQuery.GetSingletonEntity();
        var buffer = _em.GetBuffer<UnitCatalogElement>(catalogEntity);

        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].PrefabEntity == prefabEntity)
            {
                return i;
            }
        }
        return -1;
    }

    private void ReturnToCommandState()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;
        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        userState.CurrentState = UserContext.Command;
    }
    
    private void UpdateSelfDestructProgress(Entity wallEntity, bool updateButton = false)
    {
         if (!_em.HasComponent<SelfDestructTag>(wallEntity) || !_em.HasComponent<ExplosionData>(wallEntity))
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            if (updateButton && selfDestructButton) selfDestructButton.interactable = true;
            return;
        }

        var selfDestruct = _em.GetComponentData<SelfDestructTag>(wallEntity);
        var explosionData = _em.GetComponentData<ExplosionData>(wallEntity);

        if (selfDestruct.RemainingTime < 0)
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            if (updateButton && selfDestructButton) selfDestructButton.interactable = true;
            return;
        }

        if (productionProgressPanel) productionProgressPanel.SetActive(true);
        float progress = Mathf.Clamp01((explosionData.Delay - selfDestruct.RemainingTime) / explosionData.Delay);
        if (productionProgressBar) productionProgressBar.value = progress;
        if (productionProgressText) productionProgressText.text = $"{selfDestruct.RemainingTime:F1}s";
        if (updateButton && selfDestructButton) selfDestructButton.interactable = false;
    }

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

                _SelectedEntityInfoStateQuery = _em.CreateEntityQuery(typeof(SelectedEntityInfoState));
                _userStateQuery = _em.CreateEntityQuery(typeof(UserState));
                _unitCatalogQuery = _em.CreateEntityQuery(typeof(UnitCatalog)); // [추가]

                return true;
            }
        }
        return false;
    }
}