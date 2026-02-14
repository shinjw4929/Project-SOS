using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using TMPro;
using Shared;

namespace Client
{
    public class EntityCountRenderer : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI entityCountText;

        private World _clientWorld;
        private EntityManager _clientEntityManager;
        private EntityQuery _unitQuery;
        private EntityQuery _minimapDataQuery;
        private bool _isInitialized;
        private int _cachedUnitCount = -1;
        private int _cachedEnemyCount = -1;

        private void Update()
        {
            if (!_isInitialized && !TryInitClientWorld())
                return;

            if (!_clientWorld.IsCreated)
            {
                _isInitialized = false;
                return;
            }

            int unitCount = _unitQuery.CalculateEntityCount();
            int enemyCount = 0;
            if (!_minimapDataQuery.IsEmpty)
            {
                var data = _minimapDataQuery.GetSingleton<MinimapDataState>();
                if (data.EnemyPositions.IsCreated)
                    enemyCount = data.EnemyPositions.Length;
            }

            if (unitCount != _cachedUnitCount || enemyCount != _cachedEnemyCount)
            {
                _cachedUnitCount = unitCount;
                _cachedEnemyCount = enemyCount;

                if (entityCountText != null)
                    entityCountText.SetText("Units: {0}  Enemies: {1}", unitCount, enemyCount);
            }
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

                    _unitQuery = _clientEntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<UnitTag>(),
                        ComponentType.ReadOnly<GhostInstance>()
                    );

                    _minimapDataQuery = _clientEntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<MinimapDataState>()
                    );

                    _isInitialized = true;
                    return true;
                }
            }

            return false;
        }
    }
}
