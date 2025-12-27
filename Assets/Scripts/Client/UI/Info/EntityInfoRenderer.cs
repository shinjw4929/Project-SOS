using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using TMPro;
using Shared;
using Client;

public class EntityInfoUIRenderer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI moveSpeedText;
    [SerializeField] private TextMeshProUGUI attackPowerText;
    [SerializeField] private TextMeshProUGUI teamText;

    private World _clientWorld;
    private EntityManager _clientEntityManager;
    private EntityQuery _currentSelectionQuery;
    private EntityQuery _networkIdQuery;

    private void Update()
    {
        if (!TryInitClientWorld())
            return;

        if (_currentSelectionQuery.IsEmptyIgnoreFilter)
        {
            HidePanel();
            return;
        }

        var currentSelection = _currentSelectionQuery.GetSingleton<CurrentSelection>();

        if (currentSelection.SelectedCount == 0)
        {
            HidePanel();
            return;
        }

        Entity selectedEntity = currentSelection.PrimaryEntity;

        if (!_clientEntityManager.Exists(selectedEntity))
        {
            HidePanel();
            return;
        }

        // 필수 컴포넌트 확인
        if (!_clientEntityManager.HasComponent<Health>(selectedEntity) ||
            !_clientEntityManager.HasComponent<Team>(selectedEntity))
        {
            HidePanel();
            return;
        }

        Health health = _clientEntityManager.GetComponentData<Health>(selectedEntity);
        Team team = _clientEntityManager.GetComponentData<Team>(selectedEntity);

        // 선택적 컴포넌트 (건물에는 없을 수 있음)
        float moveSpeedValue = 0f;
        float attackPowerValue = 0f;

        if (_clientEntityManager.HasComponent<MovementSpeed>(selectedEntity))
            moveSpeedValue = _clientEntityManager.GetComponentData<MovementSpeed>(selectedEntity).Value;

        if (_clientEntityManager.HasComponent<CombatStatus>(selectedEntity))
            attackPowerValue = _clientEntityManager.GetComponentData<CombatStatus>(selectedEntity).AttackPower;

        int myTeamId = _networkIdQuery.GetSingleton<NetworkId>().Value;

        ShowPanel();
        healthText.text = $"HP: {health.CurrentValue:F0}/{health.MaxValue:F0}";
        moveSpeedText.text = $"SPD: {moveSpeedValue:F1}";
        attackPowerText.text = $"ATK: {attackPowerValue:F1}";

        bool isMyTeam = team.teamId == myTeamId;
        teamText.text = isMyTeam ? "MY Unit" : "Other Unit";
        teamText.color = isMyTeam ? Color.cyan : Color.red;
    }

    private bool TryInitClientWorld()
    {
        if (_clientWorld != null && _clientWorld.IsCreated)
            return true;

        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                _clientWorld = world;
                _clientEntityManager = world.EntityManager;
                _currentSelectionQuery = _clientEntityManager.CreateEntityQuery(typeof(CurrentSelection));
                _networkIdQuery = _clientEntityManager.CreateEntityQuery(typeof(NetworkId));
                return true;
            }
        }

        return false;
    }

    private void ShowPanel()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    private void HidePanel()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }
}
