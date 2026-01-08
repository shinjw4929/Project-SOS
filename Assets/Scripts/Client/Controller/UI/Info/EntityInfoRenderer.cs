using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using TMPro;
using Shared;

namespace Client
{
    public class EntityInfoUIRenderer : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI healthText;
        [SerializeField] private TextMeshProUGUI moveSpeedText;
        [SerializeField] private TextMeshProUGUI attackPowerText;
        [SerializeField] private TextMeshProUGUI teamText;

        private World _clientWorld;
        private EntityManager _entityManager;
        private EntityQuery _selectionQuery;
        private EntityQuery _networkIdQuery;

        // [최적화] 상태 캐싱용 변수 (Dirty Flags)
        private Entity _cachedEntity = Entity.Null;
        private float _cachedCurHealth = -1;
        private float _cachedMaxHealth = -1;
        private float _cachedMoveSpeed = -1;
        private float _cachedAttackPower = -1;
        private int _cachedTeamId = -2; // -1은 보통 없음, 안전하게 -2
        private int _cachedMyNetworkId = -1;

        private void Update()
        {
            if (!TryInitClientWorld()) return;

            // 1. 선택 상태 쿼리 체크
            if (_selectionQuery.IsEmptyIgnoreFilter)
            {
                SetPanelActive(false);
                return;
            }

            var selectedEntityInfoState = _selectionQuery.GetSingleton<SelectedEntityInfoState>();

            // 2. 선택된 유닛이 없으면 숨김
            if (selectedEntityInfoState.SelectedCount == 0 || selectedEntityInfoState.PrimaryEntity == Entity.Null)
            {
                SetPanelActive(false);
                ResetCache(); // 선택 해제 시 캐시 초기화
                return;
            }

            Entity currentEntity = selectedEntityInfoState.PrimaryEntity;

            // 3. 엔티티 유효성 검사
            if (!_entityManager.Exists(currentEntity) ||
                !_entityManager.HasComponent<Health>(currentEntity) ||
                !_entityManager.HasComponent<Team>(currentEntity))
            {
                SetPanelActive(false);
                return;
            }

            // 4. [최적화] 타겟이 바뀌었으면 강제 갱신을 위해 캐시 리셋
            if (_cachedEntity != currentEntity)
            {
                ResetCache();
                _cachedEntity = currentEntity;
            }

            // 5. 패널 활성화
            SetPanelActive(true);

            // 6. 데이터 가져오기 및 UI 갱신 (값이 다를 때만)
            UpdateUI(currentEntity);
        }

        private void UpdateUI(Entity entity)
        {
            // --- Health ---
            var health = _entityManager.GetComponentData<Health>(entity);
            // 현재 체력이나 최대 체력이 변했을 때만 갱신
            if (_cachedCurHealth != health.CurrentValue || _cachedMaxHealth != health.MaxValue)
            {
                _cachedCurHealth = health.CurrentValue;
                _cachedMaxHealth = health.MaxValue;
                if (healthText != null)
                    healthText.SetText("HP: {0:0}/{1:0}", _cachedCurHealth, _cachedMaxHealth);
            }

            // --- Movement Speed (Optional) ---
            float currentSpeed = 0f;
            if (_entityManager.HasComponent<MovementSpeed>(entity))
                currentSpeed = _entityManager.GetComponentData<MovementSpeed>(entity).Value;

            if (_cachedMoveSpeed != currentSpeed)
            {
                _cachedMoveSpeed = currentSpeed;
                if (moveSpeedText != null)
                    moveSpeedText.SetText("SPD: {0:1}", _cachedMoveSpeed);
            }

            // --- Attack Power (Optional) ---
            float currentAttack = 0f;
            if (_entityManager.HasComponent<CombatStats>(entity))
                currentAttack = _entityManager.GetComponentData<CombatStats>(entity).AttackPower;

            if (_cachedAttackPower != currentAttack)
            {
                _cachedAttackPower = currentAttack;
                if (attackPowerText != null)
                    attackPowerText.SetText("ATK: {0:1}", _cachedAttackPower);
            }

            // --- Team Info ---
            var team = _entityManager.GetComponentData<Team>(entity);
            
            // 내 네트워크 ID 가져오기 (싱글톤 체크 안전하게)
            int myNetId = -1;
            if (!_networkIdQuery.IsEmptyIgnoreFilter)
                myNetId = _networkIdQuery.GetSingleton<NetworkId>().Value;

            // 팀 정보나 내 ID가 바뀌었을 때만 갱신 (네트워크 ID는 처음에 바뀔 수 있음)
            if (_cachedTeamId != team.teamId || _cachedMyNetworkId != myNetId)
            {
                _cachedTeamId = team.teamId;
                _cachedMyNetworkId = myNetId;

                bool isMyTeam = (_cachedTeamId == _cachedMyNetworkId);

                if (teamText != null)
                {
                    teamText.SetText(isMyTeam ? "MY Unit" : "Other Unit");
                    teamText.color = isMyTeam ? Color.cyan : Color.red;
                }
            }
        }

        private void SetPanelActive(bool isActive)
        {
            // 이미 상태가 같다면 무시 (엔진 오버헤드 방지)
            if (panelRoot != null && panelRoot.activeSelf != isActive)
            {
                panelRoot.SetActive(isActive);
            }
        }

        private void ResetCache()
        {
            _cachedEntity = Entity.Null;
            _cachedCurHealth = -1;
            _cachedMaxHealth = -1;
            _cachedMoveSpeed = -1;
            _cachedAttackPower = -1;
            _cachedTeamId = -2;
            // _cachedMyNetworkId는 리셋하지 않아도 됨 (한 번 정해지면 거의 안 바뀜)
        }

        private bool TryInitClientWorld()
        {
            if (_clientWorld != null && _clientWorld.IsCreated) return true;

            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    _clientWorld = world;
                    _entityManager = world.EntityManager;
                    _selectionQuery = _entityManager.CreateEntityQuery(typeof(SelectedEntityInfoState));
                    _networkIdQuery = _entityManager.CreateEntityQuery(typeof(NetworkId));
                    return true;
                }
            }
            return false;
        }
    }
}