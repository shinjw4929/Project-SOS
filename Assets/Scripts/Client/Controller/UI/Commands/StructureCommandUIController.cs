using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Shared;
using Client;

/// <summary>
/// 건물 커맨드 UI 컨트롤러
/// - Function 버튼, 유닛 생산 버튼, 자폭 버튼 등 건물 명령 UI 관리
/// - AvailableUnit 버퍼 기반으로 생산 가능 유닛 표시
/// </summary>
public class StructureCommandUIController : MonoBehaviour
{
    [Header("Structure Command Panel")]
    [SerializeField] private GameObject structureCommandPanel;
    [SerializeField] private Text structureNameText;

    [Header("Command Buttons (Command 상태 - 건물 타입별 표시)")]
    [SerializeField] private GameObject commandButtonPanel;
    [SerializeField] private Button produceButton;       // 생산 시설용: Q -> 생산 메뉴
    [SerializeField] private Button selfDestructButton;  // 벽용: R -> 자폭
    // 향후 추가: rallyPointButton, autoGatherButton, attackTargetButton 등

    [Header("Production Menu (StructureActionMenu 상태)")]
    [SerializeField] private GameObject productionMenuPanel;
    [SerializeField] private CommandButton[] produceButtons; // Inspector에서 3개 할당 (Q/W/E)

    [Header("Production Progress")]
    [SerializeField] private GameObject productionProgressPanel;
    [SerializeField] private Slider productionProgressBar;
    [SerializeField] private Text productionProgressText;

    // 생산 버튼 단축키 (R은 벽 자폭용으로 예약)
    private static readonly string[] ShortcutLabels = { "Q", "W", "E" };

    // ECS 참조
    private World _clientWorld;
    private EntityManager _em;
    private EntityQuery _selectedEntityInfoStateQuery;
    private EntityQuery _userStateQuery;
    private EntityQuery _unitIndexMapQuery;

    // 캐시
    private Entity _lastProducerEntity = Entity.Null;
    private UserContext _lastState = UserContext.Dead;

    private void Start()
    {
        if (produceButton) produceButton.onClick.AddListener(OnProduceButtonClicked);
        if (selfDestructButton) selfDestructButton.onClick.AddListener(OnSelfDestructClicked);

        HideAllPanels();
    }

    private void Update()
    {
        if (!TryInitClientWorld()) return;

        // 필수 싱글톤 확인
        if (_userStateQuery.IsEmptyIgnoreFilter || _selectedEntityInfoStateQuery.IsEmptyIgnoreFilter)
        {
            HideAllPanels();
            return;
        }

        var userState = _userStateQuery.GetSingleton<UserState>();
        var selectedEntityInfoState = _selectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();

        // 1. 선택 유효성 검사
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

        _lastState = userState.CurrentState;
    }

    private void ShowCommandStateUI(Entity primaryEntity)
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(true);
        if (commandButtonPanel) commandButtonPanel.SetActive(true);
        if (productionMenuPanel) productionMenuPanel.SetActive(false);

        // 건물 타입별 명령 버튼 표시
        bool isWall = _em.HasComponent<WallTag>(primaryEntity);
        bool isProductionFacility = _em.HasComponent<ProductionFacilityTag>(primaryEntity);

        // 생산 버튼: 생산 시설만
        if (produceButton) produceButton.gameObject.SetActive(isProductionFacility);

        // 자폭 버튼: 벽만
        if (selfDestructButton) selfDestructButton.gameObject.SetActive(isWall);

        // 건물 이름 및 프로그레스 표시
        if (isWall)
        {
            if (structureNameText) structureNameText.text = "Wall";
            UpdateSelfDestructProgress(primaryEntity, updateButton: true);
        }
        else if (isProductionFacility)
        {
            string name = GetStructureDisplayName(primaryEntity);
            if (structureNameText) structureNameText.text = name;
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
        if (commandButtonPanel) commandButtonPanel.SetActive(false);

        // 생산 시설만 메뉴 상태 지원 (벽은 Command 상태에서 직접 자폭)
        if (_em.HasComponent<ProductionFacilityTag>(primaryEntity))
        {
            ShowProductionMenu(primaryEntity);
        }
        else
        {
            // 메뉴 상태를 지원하지 않는 건물은 Command로 복귀
            ReturnToCommandState();
        }
    }

    // [Action] Produce 버튼 클릭 (Q키와 동일 역할) -> 생산 메뉴 진입
    private void OnProduceButtonClicked()
    {
        if (_userStateQuery.IsEmptyIgnoreFilter) return;

        ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
        userState.CurrentState = UserContext.StructureActionMenu;
    }

    /// <summary>
    /// 생산 메뉴 표시 - AvailableUnit 버퍼 기반
    /// </summary>
    private void ShowProductionMenu(Entity producerEntity)
    {
        string name = GetStructureDisplayName(producerEntity);
        if (structureNameText) structureNameText.text = name;

        if (productionMenuPanel) productionMenuPanel.SetActive(true);

        // 메뉴 진입 시 또는 선택 엔티티 변경 시 버튼 갱신
        if (_lastState != UserContext.StructureActionMenu || _lastProducerEntity != producerEntity)
        {
            RefreshProductionButtons(producerEntity);
            _lastProducerEntity = producerEntity;
        }

        UpdateProductionProgress(producerEntity, updateButtons: true);
    }

    /// <summary>
    /// 생산 버튼 갱신 - AvailableUnit 버퍼 기반
    /// </summary>
    private void RefreshProductionButtons(Entity producerEntity)
    {
        // 모든 버튼 비활성화
        foreach (var btn in produceButtons)
        {
            if (btn != null) btn.SetActive(false);
        }

        // 버퍼 확인
        if (producerEntity == Entity.Null || !_em.Exists(producerEntity))
            return;

        if (!_em.HasBuffer<AvailableUnit>(producerEntity))
            return;

        var units = _em.GetBuffer<AvailableUnit>(producerEntity);

        for (int i = 0; i < units.Length && i < produceButtons.Length; i++)
        {
            var btn = produceButtons[i];
            if (btn == null) continue;

            Entity prefab = units[i].PrefabEntity;
            if (prefab == Entity.Null || !_em.Exists(prefab)) continue;

            // 버튼 설정
            string unitName = ProduceSelectionUtility.GetUnitName(_em, prefab);
            int cost = ProduceSelectionUtility.GetUnitCost(_em, prefab);
            bool isUnlocked = ProduceSelectionUtility.CheckUnitUnlocked(_em, prefab);
            string shortcut = i < ShortcutLabels.Length ? ShortcutLabels[i] : "";

            int localIndex = i; // 클로저용 로컬 변수
            btn.Setup(unitName, shortcut, cost, isUnlocked, () => OnProduceUnitClicked(localIndex));
            btn.SetActive(true);
        }
    }

    /// <summary>
    /// 유닛 생산 버튼 클릭 -> RPC 전송
    /// </summary>
    private void OnProduceUnitClicked(int localIndex)
    {
        if (_selectedEntityInfoStateQuery.IsEmptyIgnoreFilter) return;
        if (_unitIndexMapQuery.IsEmptyIgnoreFilter) return;

        var selection = _selectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();
        var indexMap = _unitIndexMapQuery.GetSingleton<UnitPrefabIndexMap>().Map;

        ProduceSelectionUtility.TryProduceUnit(
            _em,
            selection.PrimaryEntity,
            localIndex,
            indexMap
        );
    }

    // [Action] 자폭 버튼 클릭 (R키와 동일 역할) - Command 상태에서 직접 실행
    private void OnSelfDestructClicked()
    {
        if (_selectedEntityInfoStateQuery.IsEmptyIgnoreFilter) return;
        var selection = _selectedEntityInfoStateQuery.GetSingleton<SelectedEntityInfoState>();
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
        // 자폭 명령 후에도 Command 상태 유지 (상태 변경 없음)
    }

    /// <summary>
    /// 건물 표시 이름 조회
    /// </summary>
    private string GetStructureDisplayName(Entity entity)
    {
        if (_em.HasComponent<ResourceCenterTag>(entity)) return "Resource Center";
        if (_em.HasComponent<ProductionFacilityTag>(entity)) return "Barracks";
        if (_em.HasComponent<WallTag>(entity)) return "Wall";
        if (_em.HasComponent<TurretTag>(entity)) return "Turret";
        return "Structure";
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

    private void UpdateProductionProgress(Entity producerEntity, bool updateButtons = false)
    {
        if (!_em.HasComponent<ProductionQueue>(producerEntity))
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            return;
        }

        var queue = _em.GetComponentData<ProductionQueue>(producerEntity);

        if (queue.IsActive && queue.Duration > 0)
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(true);
            float progress = queue.Progress / queue.Duration;
            if (productionProgressBar) productionProgressBar.value = progress;
            if (productionProgressText) productionProgressText.text = $"{(int)(progress * 100)}%";

            if (updateButtons)
            {
                foreach (var btn in produceButtons)
                {
                    if (btn != null) btn.SetUnlocked(false);
                }
            }
        }
        else
        {
            if (productionProgressPanel) productionProgressPanel.SetActive(false);
            if (updateButtons)
            {
                foreach (var btn in produceButtons)
                {
                    if (btn != null) btn.SetUnlocked(true);
                }
            }
        }
    }

    private void HideAllPanels()
    {
        if (structureCommandPanel) structureCommandPanel.SetActive(false);
        if (commandButtonPanel) commandButtonPanel.SetActive(false);
        if (productionMenuPanel) productionMenuPanel.SetActive(false);
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

                _selectedEntityInfoStateQuery = _em.CreateEntityQuery(typeof(SelectedEntityInfoState));
                _userStateQuery = _em.CreateEntityQuery(typeof(UserState));
                _unitIndexMapQuery = _em.CreateEntityQuery(typeof(UnitPrefabIndexMap));

                return true;
            }
        }
        return false;
    }
}
