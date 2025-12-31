using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;
using Client;

public class StructurePreviewController : MonoBehaviour
{
    [SerializeField] private GameObject genericPreviewPrefab;
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    private GameObject _currentPreview;
    private Renderer _previewRenderer;
    private World _clientWorld;

    // 쿼리를 필드로 캐싱 (매 프레임 CreateEntityQuery 호출 방지)
    private EntityQuery _userStateQuery;
    private EntityQuery _previewStateQuery;
    private EntityQuery _gridSettingsQuery;

    private void Update()
    {
        // 1. 월드/쿼리 초기화 (한 번만) 
        // 해당 로직에서는 null 비교 허용
        if (_clientWorld == null || !_clientWorld.IsCreated)
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    _clientWorld = world;
                    var em = world.EntityManager;
                    _userStateQuery = em.CreateEntityQuery(typeof(UserState));
                    _previewStateQuery = em.CreateEntityQuery(typeof(StructurePreviewState));
                    _gridSettingsQuery = em.CreateEntityQuery(typeof(GridSettings));
                    break;
                }
            }
            // 여전히 World를 못 찾았으면 이번 프레임은 리턴
            if (_clientWorld == null) return;
        }

        // 2. TryGetSingleton으로 존재 여부 확인 및 값 획득
        if (!_userStateQuery.TryGetSingleton<UserState>(out var userState) ||
            !_previewStateQuery.TryGetSingleton<StructurePreviewState>(out var previewState))
        {
            return;
        }

        // 3. 렌더링 로직
        bool shouldShow = userState.CurrentState == UserContext.Construction && previewState.SelectedPrefab != Entity.Null;
        
        if (!shouldShow)
        {
            if (_currentPreview) DestroyPreview(); // Unity Object는 implicit bool 사용
            return;
        }

        UpdatePreviewVisuals(previewState);
    }

    private void UpdatePreviewVisuals(StructurePreviewState previewState)
    {
        var em = _clientWorld.EntityManager;

        // 프리팹 유효성 체크
        if (!em.Exists(previewState.SelectedPrefab) ||
            !em.HasComponent<StructureFootprint>(previewState.SelectedPrefab)) return;

        var footprint = em.GetComponentData<StructureFootprint>(previewState.SelectedPrefab);

        if (!_gridSettingsQuery.TryGetSingleton<GridSettings>(out var gridSettings)) return;

        // 프리뷰 객체 생성 (없을 때만)
        if (!_currentPreview) // Unity Object는 implicit bool 사용
        {
            _currentPreview = Instantiate(genericPreviewPrefab);
            _previewRenderer = _currentPreview.GetComponentInChildren<Renderer>();
        }

        // 위치/크기 계산 (Vector3 변환 최소화)
        float3 pos = GridUtility.GridToWorld(previewState.GridPosition.x, previewState.GridPosition.y, footprint.Width, footprint.Length, gridSettings);
        pos.y += footprint.Height * 0.5f + 0.05f;

        _currentPreview.transform.position = pos;
        _currentPreview.transform.localScale = new Vector3(
            footprint.Width * gridSettings.CellSize, 
            footprint.Height, 
            footprint.Length * gridSettings.CellSize
        );

        // 머티리얼 교체 (상태 변경 시에만 할당하여 오버헤드 감소)
        Material targetMat = previewState.IsValidPlacement ? validMaterial : invalidMaterial;
        if (_previewRenderer.sharedMaterial != targetMat)
        {
            _previewRenderer.material = targetMat;
        }
    }

    private void DestroyPreview()
    {
        if (_currentPreview) Destroy(_currentPreview);
        _currentPreview = null;
        _previewRenderer = null;
    }
}