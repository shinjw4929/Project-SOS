using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using TMPro;
using Shared;

namespace Client
{
    public class UserResourceInfoRenderer : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI userResourceInfoText;
        [SerializeField] private TextMeshProUGUI currentPopulationText;
        [SerializeField] private TextMeshProUGUI maxPopulationText;

        private World _clientWorld;
        private EntityManager _clientEntityManager;
        private EntityQuery _userResourcesQuery;

        // [최적화] 이전 프레임의 데이터를 저장하여 비교용으로 사용 (Dirty Flag)
        private int _cachedResources = -1;
        private int _cachedCurrentPopulation = -1;
        private int _cachedMaxPopulation = -1;

        private void Update()
        {
            // 1. 월드 초기화 체크
            if (!TryInitClientWorld())
            {
                HidePanel();
                return;
            }

            // 2. 로컬 플레이어의 자원 엔티티 조회
            using var entities = _userResourcesQuery.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
            {
                HidePanel();
                ResetCache();
                return;
            }

            // 3. 데이터 가져오기 (로컬 플레이어 자원)
            UserResources userResources = _clientEntityManager.GetComponentData<UserResources>(entities[0]);

            ShowPanel();

            // [최적화] 값이 실제로 변했을 때만 UI 갱신 수행
            UpdateUIIfChanged(ref userResources);
        }

        private void UpdateUIIfChanged(ref UserResources userResources)
        {
            // 자원 정보 갱신
            if (_cachedResources != userResources.Resources)
            {
                _cachedResources = userResources.Resources;
                if (userResourceInfoText != null)
                    userResourceInfoText.SetText("$: {0}", _cachedResources); // GC 발생 최소화
            }

            // 현재 인구 갱신
            if (_cachedCurrentPopulation != userResources.CurrentPopulation)
            {
                _cachedCurrentPopulation = userResources.CurrentPopulation;
                if (currentPopulationText != null)
                    currentPopulationText.SetText("{0}", _cachedCurrentPopulation);
            }

            // 최대 인구 갱신
            if (_cachedMaxPopulation != userResources.MaxPopulation)
            {
                _cachedMaxPopulation = userResources.MaxPopulation;
                if (maxPopulationText != null)
                    maxPopulationText.SetText("/ {0}", _cachedMaxPopulation);
            }
        }

        private bool TryInitClientWorld()
        {
            // 이미 찾았으면 바로 리턴 (가장 빈번한 경로)
            if (_clientWorld != null && _clientWorld.IsCreated)
                return true;

            // 월드 검색 (초기화 시에만 실행됨)
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    _clientWorld = world;
                    _clientEntityManager = world.EntityManager;
                    _userResourcesQuery = _clientEntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<UserResources>(),
                        ComponentType.ReadOnly<UserResourcesTag>(),
                        ComponentType.ReadOnly<GhostOwnerIsLocal>()
                    );
                    return true;
                }
            }

            return false;
        }

        private void ShowPanel()
        {
            if (panelRoot != null && !panelRoot.activeSelf)
                panelRoot.SetActive(true);
        }

        private void HidePanel()
        {
            if (panelRoot != null && panelRoot.activeSelf)
                panelRoot.SetActive(false);
        }

        private void ResetCache()
        {
            _cachedResources = -1;
            _cachedCurrentPopulation = -1;
            _cachedMaxPopulation = -1;
        }
    }
}