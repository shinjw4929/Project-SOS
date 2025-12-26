using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;
using Client;

public class BuildingPreviewController : MonoBehaviour
{
    [SerializeField] private GameObject wallPreviewPrefab;
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    private GameObject _currentPreview;
    private Renderer _previewRenderer;

    private World _clientWorld;
    private EntityQuery _buildStateQuery;
    private EntityQuery _previewStateQuery;
    private EntityQuery _gridSettingsQuery;
    private EntityQuery _buildingRefsQuery;

    private void Update()
    {
        if (!TryInitClientWorld()) return;

        if (_buildStateQuery.IsEmptyIgnoreFilter || _previewStateQuery.IsEmptyIgnoreFilter ||
            _gridSettingsQuery.IsEmptyIgnoreFilter || _buildingRefsQuery.IsEmptyIgnoreFilter)
            return;

        var userState = _buildStateQuery.GetSingleton<UserState>();
        var previewState = _previewStateQuery.GetSingleton<BuildingPreviewState>();
        var gridSettings = _gridSettingsQuery.GetSingleton<GridSettings>();
        var buildingRefs = _buildingRefsQuery.GetSingleton<BuildingEntitiesReferences>();

        if (userState.CurrentState != UserContext.Construction)
        {
            DestroyPreview();
            return;
        }

        // 프리팹에서 건물 메타데이터 조회
        Entity prefab = GetBuildingPrefab(previewState.selectedType, buildingRefs);
        if (prefab == Entity.Null || !_clientWorld.EntityManager.HasComponent<BuildingMetadata>(prefab))
        {
            DestroyPreview();
            return;
        }

        var metadata = _clientWorld.EntityManager.GetComponentData<BuildingMetadata>(prefab);
        int width = metadata.width;
        int height = metadata.height;

        if (_currentPreview == null)
        {
            _currentPreview = Instantiate(wallPreviewPrefab);
            _previewRenderer = _currentPreview.GetComponentInChildren<Renderer>();
        }

        // 위치 업데이트 (건물 타입에 맞는 크기와 높이 적용)
        float3 worldPos = GridUtility.GridToWorld(previewState.gridX, previewState.gridY, width, height, gridSettings);
        worldPos.y = GridUtility.GetBuildingYOffset(previewState.selectedType);
        _currentPreview.transform.position = worldPos;

        // 머티리얼 변경
        Material targetMat = previewState.isValidPlacement ? validMaterial : invalidMaterial;
        if (_previewRenderer != null && _previewRenderer.material != targetMat)
        {
            _previewRenderer.material = targetMat;
        }
    }

    private Entity GetBuildingPrefab(BuildingTypeEnum type, BuildingEntitiesReferences refs)
    {
        return type switch
        {
            BuildingTypeEnum.Wall => refs.wallPrefabEntity,
            BuildingTypeEnum.Barracks => refs.barracksPrefabEntity,
            _ => Entity.Null
        };
    }

    private void DestroyPreview()
    {
        if (_currentPreview != null)
        {
            Destroy(_currentPreview);
            _currentPreview = null;
            _previewRenderer = null;
        }
    }

    private bool TryInitClientWorld()
    {
        if (_clientWorld != null && _clientWorld.IsCreated) return true;

        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                _clientWorld = world;
                _buildStateQuery = world.EntityManager.CreateEntityQuery(typeof(UserState));
                _previewStateQuery = world.EntityManager.CreateEntityQuery(typeof(BuildingPreviewState));
                _gridSettingsQuery = world.EntityManager.CreateEntityQuery(typeof(GridSettings));
                _buildingRefsQuery = world.EntityManager.CreateEntityQuery(typeof(BuildingEntitiesReferences));
                return true;
            }
        }
        return false;
    }

    private void OnDestroy() => DestroyPreview();
}
