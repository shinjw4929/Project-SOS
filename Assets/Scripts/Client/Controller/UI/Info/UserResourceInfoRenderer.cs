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
        private EntityQuery _userEconomyQuery;

        // 캐싱 변수
        private int _cachedAmount = -1;
        private int _cachedCurrentSupply = -1;
        private int _cachedMaxSupply = -1;

        // [최적화] 초기화 여부 플래그
        private bool _isInitialized = false;

private void Update()
        {
            // 1. 월드 초기화 체크
            if (!_isInitialized && !TryInitClientWorld())
            {
                HidePanel();
                return;
            }
            
            if (!_clientWorld.IsCreated)
            {
                _isInitialized = false;
                HidePanel();
                return;
            }

            // [수정된 부분] 
            // IsEmptyIgnoreFilter 대신 CalculateEntityCount() 사용
            // 엔티티가 정확히 1개가 아니면(0개거나 2개 이상이면) 패널을 끄고 리턴합니다.
            // 이렇게 하면 GetSingleton()이 실패하는 것을 100% 방지할 수 있습니다.
            if (_userEconomyQuery.CalculateEntityCount() != 1)
            {
                HidePanel();
                ResetCache();
                return;
            }

            // 3. 데이터 가져오기 (위에서 1개임이 보장되었으므로 안전함)
            var currency = _userEconomyQuery.GetSingleton<UserCurrency>();
            var supply = _userEconomyQuery.GetSingleton<UserSupply>();

            ShowPanel();

            // 4. UI 갱신
            UpdateUIIfChanged(currency, supply);
        }
        private void UpdateUIIfChanged(UserCurrency currency, UserSupply supply)
        {
            // 1. 자원 정보 갱신
            if (_cachedAmount != currency.Amount)
            {
                _cachedAmount = currency.Amount;
                if (userResourceInfoText != null)
                    userResourceInfoText.SetText("$: {0}", _cachedAmount); 
            }

            // 2. 현재 인구 갱신
            if (_cachedCurrentSupply != supply.Currentvalue)
            {
                _cachedCurrentSupply = supply.Currentvalue;
                if (currentPopulationText != null)
                    currentPopulationText.SetText("{0}", _cachedCurrentSupply);
            }

            // 3. 최대 인구 갱신
            if (_cachedMaxSupply != supply.MaxValue)
            {
                _cachedMaxSupply = supply.MaxValue;
                if (maxPopulationText != null)
                    maxPopulationText.SetText("/ {0}", _cachedMaxSupply);
            }
        }

        private bool TryInitClientWorld()
        {
            // 이미 찾았다면 스킵
            if (_clientWorld != null && _clientWorld.IsCreated)
                return true;

            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    _clientWorld = world;
                    _clientEntityManager = world.EntityManager;

                    // 쿼리 생성: 로컬 소유자이면서 경제 데이터를 가진 엔티티
                    _userEconomyQuery = _clientEntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<UserCurrency>(),
                        ComponentType.ReadOnly<UserSupply>(),
                        ComponentType.ReadOnly<UserEconomyTag>(),
                        ComponentType.ReadOnly<GhostOwnerIsLocal>()
                    );
                    
                    _isInitialized = true;
                    return true;
                }
            }

            return false;
        }

        private void ShowPanel()
        {
            // activeSelf 체크는 유니티 내부 비용이 있으므로 로컬 변수 등으로 관리하면 더 좋지만,
            // 현재 구조에서는 이 정도 체크는 괜찮습니다.
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
            // UI가 꺼졌다가 다시 켜질 때 값이 같아도 텍스트를 갱신하도록 강제하기 위함
            _cachedAmount = -1;
            _cachedCurrentSupply = -1;
            _cachedMaxSupply = -1;
        }
    }
}