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
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI healthText;
        [SerializeField] private TextMeshProUGUI defenseText;
        [SerializeField] private TextMeshProUGUI moveSpeedText;
        [SerializeField] private TextMeshProUGUI attackPowerText;
        [SerializeField] private TextMeshProUGUI teamText;

        private World _clientWorld;
        private EntityManager _entityManager;
        private EntityQuery _selectionQuery;
        private EntityQuery _networkIdQuery;

        // [최적화] 상태 캐싱용 변수 (Dirty Flags)
        private Entity _cachedEntity = Entity.Null;
        private string _cachedName = null;
        private float _cachedCurHealth = -1;
        private float _cachedMaxHealth = -1;
        private bool _cachedIsInvincible = false;
        private float _cachedDefense = -1;
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
            if (!_entityManager.Exists(currentEntity))
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
            // --- Name (태그 기반) ---
            string currentName = GetEntityName(entity);
            if (_cachedName != currentName)
            {
                _cachedName = currentName;
                if (nameText != null)
                    nameText.SetText(_cachedName);
            }

            // --- Health / 무적 ---
            bool isInvincible = !_entityManager.HasComponent<Health>(entity);
            if (isInvincible)
            {
                if (!_cachedIsInvincible)
                {
                    _cachedIsInvincible = true;
                    _cachedCurHealth = -1;
                    _cachedMaxHealth = -1;
                    if (healthText != null)
                    {
                        healthText.SetText("Invincible");
                        healthText.color = Color.yellow;
                    }
                }
            }
            else
            {
                var health = _entityManager.GetComponentData<Health>(entity);
                if (_cachedIsInvincible || _cachedCurHealth != health.CurrentValue || _cachedMaxHealth != health.MaxValue)
                {
                    _cachedIsInvincible = false;
                    _cachedCurHealth = health.CurrentValue;
                    _cachedMaxHealth = health.MaxValue;
                    if (healthText != null)
                    {
                        healthText.SetText("HP: {0:0}/{1:0}", _cachedCurHealth, _cachedMaxHealth);
                        healthText.color = Color.white;
                    }
                }
            }

            // --- Defense (Optional) ---
            float currentDefense = 0f;
            if (_entityManager.HasComponent<Defense>(entity))
                currentDefense = _entityManager.GetComponentData<Defense>(entity).Value;

            if (_cachedDefense != currentDefense)
            {
                _cachedDefense = currentDefense;
                if (defenseText != null)
                    defenseText.SetText("DEF: {0:0}", _cachedDefense);
            }

            // --- Movement Speed (Optional) ---
            float currentSpeed = 0f;
            if (_entityManager.HasComponent<MovementDynamics>(entity))
                currentSpeed = _entityManager.GetComponentData<MovementDynamics>(entity).MaxSpeed;

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

            // --- Team Info (Optional) ---
            if (_entityManager.HasComponent<Team>(entity))
            {
                var team = _entityManager.GetComponentData<Team>(entity);

                // 내 네트워크 ID 가져오기 (싱글톤 체크 안전하게)
                int myNetId = -1;
                if (!_networkIdQuery.IsEmptyIgnoreFilter)
                    myNetId = _networkIdQuery.GetSingleton<NetworkId>().Value;

                // 팀 정보나 내 ID가 바뀌었을 때만 갱신
                if (_cachedTeamId != team.teamId || _cachedMyNetworkId != myNetId)
                {
                    _cachedTeamId = team.teamId;
                    _cachedMyNetworkId = myNetId;

                    if (teamText != null)
                    {
                        if (_cachedTeamId < 0)
                        {
                            // 음수 팀 ID = 적
                            teamText.SetText("Enemy");
                            teamText.color = Color.red;
                        }
                        else if (_cachedTeamId == _cachedMyNetworkId)
                        {
                            teamText.SetText("MY Unit");
                            teamText.color = Color.cyan;
                        }
                        else
                        {
                            teamText.SetText("Other Unit");
                            teamText.color = Color.yellow;
                        }
                    }
                }
            }
            else
            {
                // Team 컴포넌트가 없는 엔티티 (리소스 노드 등)
                if (_cachedTeamId != -99)
                {
                    _cachedTeamId = -99;
                    if (teamText != null)
                    {
                        teamText.SetText("Neutral");
                        teamText.color = Color.gray;
                    }
                }
            }
        }

        private string GetEntityName(Entity entity)
        {
            // Unit types
            if (_entityManager.HasComponent<HeroTag>(entity)) return "Hero";
            if (_entityManager.HasComponent<WorkerTag>(entity)) return "Worker";
            if (_entityManager.HasComponent<SwordsmanTag>(entity)) return "Swordsman";
            if (_entityManager.HasComponent<TrooperTag>(entity)) return "Trooper";
            if (_entityManager.HasComponent<SniperTag>(entity)) return "Sniper";

            // Structure types - 구체적인 태그 먼저 체크 (ResourceCenter는 ProductionFacilityTag도 가짐)
            if (_entityManager.HasComponent<ResourceCenterTag>(entity)) return "Resource Center";
            if (_entityManager.HasComponent<WallTag>(entity)) return "Wall";
            if (_entityManager.HasComponent<ProductionFacilityTag>(entity)) return "Barracks";
            if (_entityManager.HasComponent<TurretTag>(entity)) return "Turret";

            // Other entities
            if (_entityManager.HasComponent<EnemyTag>(entity)) return "Enemy";
            if (_entityManager.HasComponent<ResourceNodeTag>(entity)) return "Ore Vein";

            // Fallback
            if (_entityManager.HasComponent<UnitTag>(entity)) return "Unit";
            if (_entityManager.HasComponent<StructureTag>(entity)) return "Structure";

            return "Unknown";
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
            _cachedName = null;
            _cachedCurHealth = -1;
            _cachedMaxHealth = -1;
            _cachedIsInvincible = false;
            _cachedDefense = -1;
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