using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;
using Client;
using Unity.Physics;
using Material = UnityEngine.Material;

namespace Client
{
    public class StructurePreviewController : MonoBehaviour
    {
        [SerializeField] private Material carpetValidMaterial;      // 파란색: 사거리 내 건설 가능
        [SerializeField] private Material carpetInvalidMaterial;    // 빨간색: 건설 불가
        [SerializeField] private Material carpetOutOfRangeMaterial; // 노란색: 사거리 밖 (이동 후 건설)
    
        // 캐싱된 컴포넌트들
        private GameObject _currentPreview;
        private Transform _previewTransform; // 매 프레임 .transform 접근 방지
        private World _clientWorld;
    
        // 양탄자 오브젝트 (별도 관리)
        private GameObject _carpetObject;
        private Transform _carpetTransform;
        private MeshRenderer _carpetRenderer;
    
        // 데이터 캐싱 (최적화 핵심)
        private int _cachedPrefabIndex = -1;
        private StructureFootprint _cachedFootprint;
    
        // 쿼리 필드
        private EntityQuery _userStateQuery;
        private EntityQuery _previewStateQuery;
        private EntityQuery _gridSettingsQuery;
        private EntityQuery _prefabStoreQuery;
        private EntityQuery _localTeamQuery;
    
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
                    _localTeamQuery = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HeroTag>(),
                        ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                        ComponentType.ReadOnly<Team>()
                    );
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
                _currentPreview.hideFlags = HideFlags.DontSave; // 에디터 저장 시 제외 (Assertion 에러 방지)
    
                // 2. 컴포넌트 캐싱 (매 프레임 GetComponent 방지)
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
    
                // 4. GridSettings 조회 (양탄자 크기 계산용)
                if (!_gridSettingsQuery.TryGetSingleton<GridSettings>(out var tempGridSettings)) return;
    
                // 5. 양탄자 생성 (별도 오브젝트로 관리)
                CreateCarpet(_cachedFootprint.Width, _cachedFootprint.Length, tempGridSettings.CellSize);
    
                // 6. 팀 색상 틴트 적용
                ApplyTeamColorTint();
    
                // 7. 인덱스 업데이트
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
    
            // C. 위치 적용
            _previewTransform.position = pos; // 캐시된 Transform 사용
    
            // 양탄자 위치 (지면 + 약간 위)
            if (_carpetTransform)
            {
                _carpetTransform.position = new Vector3(pos.x, 0.05f, pos.z);
            }
    
            // 양탄자 색상 로직: PlacementStatus 기반
            Material targetMat = previewState.Status switch
            {
                PlacementStatus.ValidInRange => carpetValidMaterial,      // 파란색
                PlacementStatus.ValidOutOfRange => carpetOutOfRangeMaterial, // 노란색
                _ => carpetInvalidMaterial                                   // 빨간색
            };
    
            // 양탄자 머티리얼 교체 체크 (SharedMaterial 비교가 빠름)
            if (_carpetRenderer && _carpetRenderer.sharedMaterial != targetMat)
            {
                _carpetRenderer.material = targetMat;
            }
        }
    
        private void DestroyPreview()
        {
            if (_currentPreview) Destroy(_currentPreview);
            if (_carpetObject) Destroy(_carpetObject);
            _currentPreview = null;
            _previewTransform = null;
            _carpetObject = null;
            _carpetTransform = null;
            _carpetRenderer = null;
            _cachedPrefabIndex = -1; // 인덱스 초기화하여 다음에 다시 생성되게 함
        }
    
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    
        private void ApplyTeamColorTint()
        {
            if (!_currentPreview) return;
    
            int teamId = 0;
            if (_localTeamQuery.CalculateEntityCount() == 1)
            {
                teamId = _localTeamQuery.GetSingleton<Team>().teamId;
            }
    
            var teamColor = TeamColorPalette.GetTeamColor(teamId);
            var tint = new Color(teamColor.x, teamColor.y, teamColor.z, teamColor.w);
    
            foreach (var renderer in _currentPreview.GetComponentsInChildren<MeshRenderer>())
            {
                var sharedMat = renderer.sharedMaterial;
                if (sharedMat == null || !sharedMat.HasProperty(BaseColorId)) continue;

                Color originalColor = sharedMat.GetColor(BaseColorId);
                renderer.material.SetColor(BaseColorId, originalColor * tint);
            }
        }
    
        private void CreateCarpet(int width, int length, float cellSize)
        {
            _carpetObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _carpetObject.name = "GridCarpet";
            _carpetObject.hideFlags = HideFlags.DontSave;
    
            _carpetTransform = _carpetObject.transform;
    
            // Collider 제거
            Destroy(_carpetObject.GetComponent<UnityEngine.Collider>());
    
            // Quad를 바닥에 눕히기 (XY평면 → XZ평면)
            _carpetTransform.rotation = Quaternion.Euler(90f, 0f, 0f);
    
            // 그리드 크기에 맞게 스케일 조정
            float totalWidth = width * cellSize;
            float totalLength = length * cellSize;
            _carpetTransform.localScale = new Vector3(totalWidth, totalLength, 1f);
    
            _carpetRenderer = _carpetObject.GetComponent<MeshRenderer>();
        }
    }
}