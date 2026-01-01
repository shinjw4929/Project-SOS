using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;
using Client;
using Unity.Physics;
using Material = UnityEngine.Material;

public class StructurePreviewController : MonoBehaviour
{
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    // 캐싱된 컴포넌트들
    private GameObject _currentPreview;
    private Transform _previewTransform; // 매 프레임 .transform 접근 방지
    private Renderer _previewRenderer;
    private World _clientWorld;

    // 데이터 캐싱 (최적화 핵심)
    private int _cachedPrefabIndex = -1;
    private StructureFootprint _cachedFootprint;
    private Vector3 _cachedScale; // 매 프레임 new Vector3 방지

    // 쿼리 필드
    private EntityQuery _userStateQuery;
    private EntityQuery _previewStateQuery;
    private EntityQuery _gridSettingsQuery;
    private EntityQuery _prefabStoreQuery;

    private void Update()
    {
        // 1. 월드 초기화 (기존과 동일)
        if (_clientWorld == null || !_clientWorld.IsCreated)
        {
            InitializeWorld();
            if (_clientWorld == null) return;
        }

        // 2. 싱글톤 체크 (빠른 리턴)
        if (!_userStateQuery.TryGetSingleton<UserState>(out var userState) ||
            !_previewStateQuery.TryGetSingleton<StructurePreviewState>(out var previewState) ||
            _prefabStoreQuery.IsEmpty)
        {
            return;
        }

        // 3. 표시 여부 결정
        bool shouldShow = userState.CurrentState == UserContext.Construction && 
                          previewState.SelectedPrefab != Entity.Null;
        
        if (!shouldShow)
        {
            // 프리뷰가 켜져 있었다면 끔
            if (_currentPreview) DestroyPreview(); 
            return;
        }

        UpdatePreviewVisuals(previewState);
    }

    private void InitializeWorld()
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
                _prefabStoreQuery = em.CreateEntityQuery(typeof(StructurePrefabStore));
                break;
            }
        }
    }

    private void UpdatePreviewVisuals(StructurePreviewState previewState)
    {
        // A. 인덱스가 바뀌었는지 확인 (Dirty Flag 패턴)
        // 이 블록 안에서만 "무거운 작업(Instantiate, ECS 조회, 문자열 등)"을 수행합니다.
        if (_cachedPrefabIndex != previewState.SelectedPrefabIndex)
        {
            // Managed Component 가져오기 (인덱스 바뀔 때만 호출해도 충분)
            var prefabStore = _prefabStoreQuery.GetSingleton<StructurePrefabStore>();

            int newIndex = previewState.SelectedPrefabIndex;

            // 유효성 검사
            if (newIndex < 0 || newIndex >= prefabStore.Prefabs.Length)
            {
                DestroyPreview(); // 유효하지 않으면 제거
                return;
            }

            GameObject targetPrefab = prefabStore.Prefabs[newIndex];
            if (targetPrefab == null) return;

            // 1. 기존 프리뷰 제거 및 생성
            if (_currentPreview) Destroy(_currentPreview);
            
            _currentPreview = Instantiate(targetPrefab);
            _currentPreview.name = targetPrefab.name + "(Preview)"; // 스트링 할당은 여기서 딱 한 번만
            
            // 2. 컴포넌트 캐싱 (매 프레임 GetComponent 방지)
            _previewRenderer = _currentPreview.GetComponentInChildren<Renderer>();
            _previewTransform = _currentPreview.transform;

            // 3. ECS 데이터 조회 (여기서만 수행!)
            var em = _clientWorld.EntityManager;
            if (em.Exists(previewState.SelectedPrefab) && 
                em.HasComponent<StructureFootprint>(previewState.SelectedPrefab))
            {
                _cachedFootprint = em.GetComponentData<StructureFootprint>(previewState.SelectedPrefab);
            }
            else
            {
                // 데이터가 없으면 기본값 혹은 리턴 처리
                return; 
            }

            // 4. GridSettings는 여기서 필요 (스케일 계산용)
            if (!_gridSettingsQuery.TryGetSingleton<GridSettings>(out var tempGridSettings)) return;

            // 5. 스케일 미리 계산 (Scale은 변하지 않음)
            _cachedScale = new Vector3(
                _cachedFootprint.Width * tempGridSettings.CellSize,
                _cachedFootprint.Height,
                _cachedFootprint.Length * tempGridSettings.CellSize
            );
            
            _previewTransform.localScale = _cachedScale;

            // 6. 인덱스 업데이트
            _cachedPrefabIndex = newIndex;
        }

        // =========================================================
        // 매 프레임 실행되는 영역 (최대한 가볍게)
        // =========================================================
        
        if (!_currentPreview) return; // 방어 코드

        // B. 위치 계산 (캐시된 Footprint 사용)
        if (!_gridSettingsQuery.TryGetSingleton<GridSettings>(out var gridSettings)) return;

        float3 pos = GridUtility.GridToWorld(
            previewState.GridPosition.x, 
            previewState.GridPosition.y, 
            _cachedFootprint.Width,  // 캐시값 사용
            _cachedFootprint.Length, // 캐시값 사용
            gridSettings
        );
        
        // 높이값 적용
        pos.y += _cachedFootprint.Height * 0.5f + 0.05f;

        // C. 위치 및 머티리얼 적용
        _previewTransform.position = pos; // 캐시된 Transform 사용

        Material targetMat = previewState.IsValidPlacement ? validMaterial : invalidMaterial;
        
        // 머티리얼 교체 체크 (SharedMaterial 비교가 빠름)
        if (_previewRenderer && _previewRenderer.sharedMaterial != targetMat)
        {
            _previewRenderer.material = targetMat;
        }
    }

    private void DestroyPreview()
    {
        if (_currentPreview) Destroy(_currentPreview);
        _currentPreview = null;
        _previewRenderer = null;
        _previewTransform = null;
        _cachedPrefabIndex = -1; // 인덱스 초기화하여 다음에 다시 생성되게 함
    }
}