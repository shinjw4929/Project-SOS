using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// 유닛 커맨드 UI 컨트롤러
    /// - Build 버튼, 건물 선택 버튼 등 RTS 커맨드 UI 관리
    /// - AvailableStructure 버퍼 기반으로 건설 가능 건물 표시
    /// </summary>
    public class UnitCommandUIController : MonoBehaviour
    {
        [Header("Main Panel")]
        [SerializeField] private GameObject mainCommandPanel;
        [SerializeField] private Button buildButton;

        [Header("Build Menu Panel")]
        [SerializeField] private GameObject buildMenuPanel;
        [SerializeField] private CommandButton[] buildButtons; // Inspector에서 4개 할당

        private static readonly string[] ShortcutLabels = { "Q", "W", "E", "R" };

        // ECS 참조
        private World _clientWorld;
        private EntityManager _em;
        private EntityQuery _selectedEntityInfoQuery;
        private EntityQuery _userStateQuery;
        private EntityQuery _selectionInputQuery;
        private EntityQuery _previewStateQuery;
        private EntityQuery _indexMapQuery;

        // 캐시
        private Entity _lastBuilderEntity = Entity.Null;
        private UserContext _lastState = UserContext.Dead;

        private void Start()
        {
            if (buildButton)
                buildButton.onClick.AddListener(OnBuildButtonClicked);

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
                    HandleCommandState();
                    break;

                case UserContext.BuildMenu:
                    HandleBuildMenuState();
                    break;

                case UserContext.Construction:
                case UserContext.StructureActionMenu:
                default:
                    HideAllPanels();
                    break;
            }

            _lastState = userState.CurrentState;
        }

        private void HandleCommandState()
        {
            if (CanShowBuildButton())
            {
                if (mainCommandPanel) mainCommandPanel.SetActive(true);
                if (buildMenuPanel) buildMenuPanel.SetActive(false);
            }
            else
            {
                HideAllPanels();
            }
        }

        private void HandleBuildMenuState()
        {
            if (mainCommandPanel) mainCommandPanel.SetActive(false);
            if (buildMenuPanel) buildMenuPanel.SetActive(true);

            // BuildMenu 진입 시 또는 선택 유닛 변경 시 버튼 갱신
            var selection = _selectedEntityInfoQuery.GetSingleton<SelectedEntityInfoState>();
            if (_lastState != UserContext.BuildMenu || _lastBuilderEntity != selection.PrimaryEntity)
            {
                RefreshBuildMenu(selection.PrimaryEntity);
                _lastBuilderEntity = selection.PrimaryEntity;
            }
        }

        /// <summary>
        /// 건설 메뉴 버튼 갱신 - AvailableStructure 버퍼 기반
        /// </summary>
        private void RefreshBuildMenu(Entity builderEntity)
        {
            // 모든 버튼 비활성화
            foreach (var btn in buildButtons)
            {
                if (btn != null) btn.SetActive(false);
            }

            // 버퍼 확인
            if (builderEntity == Entity.Null || !_em.Exists(builderEntity))
                return;

            if (!_em.HasBuffer<AvailableStructure>(builderEntity))
                return;

            var structures = _em.GetBuffer<AvailableStructure>(builderEntity);

            for (int i = 0; i < structures.Length && i < buildButtons.Length; i++)
            {
                var btn = buildButtons[i];
                if (btn == null) continue;

                Entity prefab = structures[i].PrefabEntity;
                if (prefab == Entity.Null || !_em.Exists(prefab)) continue;

                // 버튼 설정
                string name = BuildSelectionUtility.GetStructureName(_em, prefab);
                int cost = BuildSelectionUtility.GetStructureCost(_em, prefab);
                bool isUnlocked = BuildSelectionUtility.CheckTechUnlocked(_em, prefab);
                string shortcut = i < ShortcutLabels.Length ? ShortcutLabels[i] : "";

                int localIndex = i; // 클로저용 로컬 변수
                btn.Setup(name, shortcut, cost, isUnlocked, () => OnBuildStructureClicked(localIndex));
                btn.SetActive(true);
            }
        }

        /// <summary>
        /// Build 버튼 클릭 -> BuildMenu 진입
        /// </summary>
        private void OnBuildButtonClicked()
        {
            if (!CanShowBuildButton()) return;
            if (_userStateQuery.IsEmptyIgnoreFilter || _selectionInputQuery.IsEmptyIgnoreFilter) return;

            ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
            ref var selectionInput = ref _selectionInputQuery.GetSingletonRW<UserSelectionInputState>().ValueRW;

            selectionInput.Phase = SelectionPhase.Idle;
            userState.CurrentState = UserContext.BuildMenu;
        }

        /// <summary>
        /// 건물 버튼 클릭 -> Construction 모드 진입
        /// </summary>
        private void OnBuildStructureClicked(int localIndex)
        {
            if (_userStateQuery.IsEmptyIgnoreFilter || _previewStateQuery.IsEmptyIgnoreFilter)
                return;

            var selection = _selectedEntityInfoQuery.GetSingleton<SelectedEntityInfoState>();
            if (selection.PrimaryEntity == Entity.Null)
                return;

            // 인덱스 맵 가져오기
            if (_indexMapQuery.IsEmptyIgnoreFilter)
                return;

            var indexMap = _indexMapQuery.GetSingleton<StructurePrefabIndexMap>().Map;

            // 공통 유틸리티 사용
            ref var userState = ref _userStateQuery.GetSingletonRW<UserState>().ValueRW;
            ref var previewState = ref _previewStateQuery.GetSingletonRW<StructurePreviewState>().ValueRW;

            BuildSelectionUtility.TrySelectStructure(
                _em,
                selection.PrimaryEntity,
                localIndex,
                ref userState,
                ref previewState,
                indexMap
            );
        }

        /// <summary>
        /// Build 버튼 표시 가능 여부 확인
        /// </summary>
        private bool CanShowBuildButton()
        {
            if (_selectedEntityInfoQuery.IsEmptyIgnoreFilter) return false;

            var selection = _selectedEntityInfoQuery.GetSingleton<SelectedEntityInfoState>();

            // 1개만 선택, 내 소유
            if (selection.SelectedCount != 1 || !selection.IsOwnedSelection) return false;

            // BuilderTag 확인
            if (selection.PrimaryEntity == Entity.Null || !_em.Exists(selection.PrimaryEntity))
                return false;

            return _em.HasComponent<BuilderTag>(selection.PrimaryEntity);
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

                    _selectedEntityInfoQuery = _em.CreateEntityQuery(typeof(SelectedEntityInfoState));
                    _userStateQuery = _em.CreateEntityQuery(typeof(UserState));
                    _selectionInputQuery = _em.CreateEntityQuery(typeof(UserSelectionInputState));
                    _previewStateQuery = _em.CreateEntityQuery(typeof(StructurePreviewState));
                    _indexMapQuery = _em.CreateEntityQuery(typeof(StructurePrefabIndexMap));

                    return true;
                }
            }

            return false;
        }
    }
}
