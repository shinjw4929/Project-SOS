using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;
using Client;

public class StructurePreviewController : MonoBehaviour
{
    [SerializeField] private GameObject genericPreviewPrefab; // 범용 큐브 프리팹
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    private GameObject _currentPreview;
    private Renderer _previewRenderer;

    private World _clientWorld;
    private EntityQuery _userStateQuery;
    private EntityQuery _previewStateQuery;
    private EntityQuery _gridSettingsQuery;

    private void Update()
    {
        if (!TryInitClientWorld()) return;

        if (_userStateQuery.IsEmptyIgnoreFilter || _previewStateQuery.IsEmptyIgnoreFilter || _gridSettingsQuery.IsEmptyIgnoreFilter)
            return;

        var userState = _userStateQuery.GetSingleton<UserState>();
        var previewState = _previewStateQuery.GetSingleton<StructurePreviewState>();
        var gridSettings = _gridSettingsQuery.GetSingleton<GridSettings>();
        var entityManager = _clientWorld.EntityManager;

        // 1. 표시 조건 체크
        if (userState.CurrentState != UserContext.Construction || previewState.SelectedPrefab == Entity.Null)
        {
            DestroyPreview();
            return;
        }

        // 2. 프리팹 데이터 안전하게 가져오기 (3중 체크)
        Entity prefab = previewState.SelectedPrefab;
        if (!entityManager.Exists(prefab) || !entityManager.HasComponent<StructureFootprint>(prefab))
        {
            DestroyPreview();
            return;
        }

        var footprint = entityManager.GetComponentData<StructureFootprint>(prefab);
        int width = footprint.Width;
        int length = footprint.Length;
        float height = footprint.Height;

        // 3. 프리뷰 객체 생성
        if (_currentPreview == null)
        {
            _currentPreview = Instantiate(genericPreviewPrefab);
            _previewRenderer = _currentPreview.GetComponentInChildren<Renderer>();
        }

        // 4. 위치 및 크기 업데이트
        float3 worldPos = GridUtility.GridToWorld(previewState.GridPosition.x, previewState.GridPosition.y, width, length, gridSettings);

        // [핵심 로직]
        // Cube(Scale=1)의 피벗은 정중앙이므로, 높이의 절반만큼 올려야 바닥에 닿음
        // 예: 높이가 5m면, y좌표를 2.5m 올려야 바닥(0)에 섬
        worldPos.y += height * 0.5f; 

        // (옵션) 바닥 Z-Fighting 방지를 위해 아주 살짝만 더 띄우고 싶다면
        worldPos.y += 0.05f; 

        _currentPreview.transform.position = worldPos;

        // [스케일 적용]
        // X, Z는 그리드 크기 기준, Y는 설정한 높이 기준
        _currentPreview.transform.localScale = new Vector3(
            width * gridSettings.CellSize, 
            height,  // [변경] 1.0f 대신 실제 높이 적용
            length * gridSettings.CellSize
        );
        
        // 5. 색상 변경
        Material targetMat = previewState.IsValidPlacement ? validMaterial : invalidMaterial;
        if (_previewRenderer != null && _previewRenderer.material != targetMat)
        {
            _previewRenderer.material = targetMat;
        }
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
                _userStateQuery = world.EntityManager.CreateEntityQuery(typeof(UserState));
                _previewStateQuery = world.EntityManager.CreateEntityQuery(typeof(StructurePreviewState));
                _gridSettingsQuery = world.EntityManager.CreateEntityQuery(typeof(GridSettings));
                return true;
            }
        }
        return false;
    }

    private void OnDestroy() => DestroyPreview();
}
