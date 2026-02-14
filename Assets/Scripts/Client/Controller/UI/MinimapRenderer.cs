using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Shared;


namespace Client
{
    /// <summary>
    /// UI 기반 미니맵 렌더러. Texture2D에 적/아군/건물/히어로 위치를 점으로 렌더링.
    /// 적: MinimapDataState(RPC), 아군/건물: Ghost 엔티티 직접 쿼리.
    /// 카메라 뷰포트 사각형 표시 + 좌클릭 드래그로 카메라 이동 (건설 모드 시 차단).
    /// </summary>
    public class MinimapRenderer : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private RawImage minimapImage;

        [Header("Settings")]
        [SerializeField] private int textureSize = 256;
        [SerializeField] private float updateInterval = 0.1f;

        [Header("Colors")]
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.15f, 0.05f, 0.85f);
        [SerializeField] private Color enemyColor = new Color(1f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color unitColor = new Color(0.2f, 1f, 0.2f, 1f);
        [SerializeField] private Color structureColor = new Color(0.3f, 0.5f, 1f, 1f);
        [SerializeField] private Color heroColor = Color.white;
        [SerializeField] private Color resourceColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Viewport Indicator")]
        [SerializeField] private Color viewportColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] private int viewportLineWidth = 1;

        private Texture2D _texture;
        private Color32[] _pixels;
        private float _timer;

        private World _clientWorld;
        private EntityQuery _minimapDataQuery;
        private EntityQuery _unitQuery;
        private EntityQuery _structureQuery;
        private EntityQuery _heroQuery;
        private EntityQuery _resourceQuery;
        private EntityQuery _cameraSettingsQuery;
        private EntityQuery _cameraStateQuery;
        private EntityQuery _userStateQuery;

        private float2 _mapMin;
        private float2 _mapMax;
        private bool _boundsInitialized;
        private bool _isDraggingMinimap;

        private void Start()
        {
            _texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _pixels = new Color32[textureSize * textureSize];

            if (minimapImage != null)
                minimapImage.texture = _texture;
        }

        private void Update()
        {
            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                InitializeWorld();
                if (_clientWorld == null) return;
            }

            if (!_boundsInitialized)
            {
                InitializeBounds();
                if (!_boundsInitialized) return;
            }

            // 입력은 매 프레임 체크 (타이머와 무관)
            HandleInput();

            _timer += Time.deltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;

            RenderMinimap();
        }

        private void InitializeWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    _clientWorld = world;
                    var em = world.EntityManager;

                    _minimapDataQuery = em.CreateEntityQuery(typeof(MinimapDataState));

                    _unitQuery = em.CreateEntityQuery(
                        typeof(UnitTag),
                        typeof(LocalTransform),
                        typeof(GhostInstance));

                    _structureQuery = em.CreateEntityQuery(
                        typeof(StructureTag),
                        typeof(LocalTransform));

                    _heroQuery = em.CreateEntityQuery(
                        typeof(HeroTag),
                        typeof(LocalTransform));

                    _resourceQuery = em.CreateEntityQuery(
                        typeof(ResourceNodeTag),
                        typeof(LocalTransform));

                    _cameraSettingsQuery = em.CreateEntityQuery(typeof(CameraSettings));
                    _cameraStateQuery = em.CreateEntityQuery(typeof(CameraState));
                    _userStateQuery = em.CreateEntityQuery(typeof(UserState));
                    break;
                }
            }
        }

        private void InitializeBounds()
        {
            if (_cameraSettingsQuery == null || _cameraSettingsQuery.IsEmpty) return;

            var settings = _cameraSettingsQuery.GetSingleton<CameraSettings>();
            _mapMin = settings.MapBoundsMin;
            _mapMax = settings.MapBoundsMax;
            _boundsInitialized = true;
        }

        private void RenderMinimap()
        {
            // 배경 클리어
            var bg = (Color32)backgroundColor;
            for (int i = 0; i < _pixels.Length; i++)
                _pixels[i] = bg;

            // 적 (MinimapDataState RPC 데이터)
            if (_minimapDataQuery != null && !_minimapDataQuery.IsEmpty)
            {
                var data = _minimapDataQuery.GetSingleton<MinimapDataState>();
                if (data.EnemyPositions.IsCreated)
                {
                    var positions = data.EnemyPositions;
                    for (int i = 0; i < positions.Length; i++)
                        DrawDot(positions[i], enemyColor, 1);
                }
            }

            // 자원 노드
            DrawEntities(_resourceQuery, resourceColor, 2);

            // 아군 유닛
            DrawEntities(_unitQuery, unitColor, 2);

            // 건물
            DrawEntities(_structureQuery, structureColor, 3);

            // 히어로 (마지막 = 최상위)
            DrawEntities(_heroQuery, heroColor, 4);

            // 카메라 뷰포트 사각형 (최상위 레이어)
            DrawCameraViewport();

            _texture.SetPixels32(_pixels);
            _texture.Apply();
        }

        private void DrawCameraViewport()
        {
            if (_cameraStateQuery == null || _cameraStateQuery.IsEmpty) return;

            var cam = Camera.main;
            if (cam == null) return;

            var cameraState = _cameraStateQuery.GetSingleton<CameraState>();
            var viewHalf = cameraState.ViewHalfExtent;
            if (viewHalf.x <= 0 || viewHalf.y <= 0) return;

            var camPos = cam.transform.position;
            float2 mapSize = _mapMax - _mapMin;
            if (mapSize.x <= 0 || mapSize.y <= 0) return;

            // 카메라 중심 → 텍스처 좌표
            float uMin = (camPos.x - viewHalf.x - _mapMin.x) / mapSize.x;
            float uMax = (camPos.x + viewHalf.x - _mapMin.x) / mapSize.x;
            float vMin = (camPos.z - viewHalf.y - _mapMin.y) / mapSize.y;
            float vMax = (camPos.z + viewHalf.y - _mapMin.y) / mapSize.y;

            int x0 = Mathf.Clamp((int)(uMin * textureSize), 0, textureSize - 1);
            int x1 = Mathf.Clamp((int)(uMax * textureSize), 0, textureSize - 1);
            int y0 = Mathf.Clamp((int)(vMin * textureSize), 0, textureSize - 1);
            int y1 = Mathf.Clamp((int)(vMax * textureSize), 0, textureSize - 1);

            var c32 = (Color32)viewportColor;

            // 상하 수평선
            DrawHLine(x0, x1, y0, c32, viewportLineWidth);
            DrawHLine(x0, x1, y1, c32, viewportLineWidth);

            // 좌우 수직선
            DrawVLine(x0, y0, y1, c32, viewportLineWidth);
            DrawVLine(x1, y0, y1, c32, viewportLineWidth);
        }

        private void DrawHLine(int x0, int x1, int y, Color32 color, int width)
        {
            int half = width / 2;
            for (int w = -half; w <= half; w++)
            {
                int py = y + w;
                if (py < 0 || py >= textureSize) continue;
                for (int px = x0; px <= x1; px++)
                {
                    if (px >= 0 && px < textureSize)
                        _pixels[py * textureSize + px] = color;
                }
            }
        }

        private void DrawVLine(int x, int y0, int y1, Color32 color, int width)
        {
            int half = width / 2;
            for (int w = -half; w <= half; w++)
            {
                int px = x + w;
                if (px < 0 || px >= textureSize) continue;
                for (int py = y0; py <= y1; py++)
                {
                    if (py >= 0 && py < textureSize)
                        _pixels[py * textureSize + px] = color;
                }
            }
        }

        private void HandleInput()
        {
            // 건설 모드에서는 미니맵 카메라 이동 차단
            if (_userStateQuery != null && !_userStateQuery.IsEmpty)
            {
                var userState = _userStateQuery.GetSingleton<UserState>();
                if (userState.CurrentState == UserContext.Construction)
                {
                    _isDraggingMinimap = false;
                    return;
                }
            }

            var mouse = Mouse.current;
            if (mouse == null || minimapImage == null) return;

            var leftButton = mouse.leftButton;

            // 드래그 시작: 좌클릭 누른 순간 + 미니맵 영역 내
            if (leftButton.wasPressedThisFrame)
            {
                Vector2 pressPos = mouse.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(minimapImage.rectTransform, pressPos))
                {
                    _isDraggingMinimap = true;
                    SwitchToEdgePanIfNeeded();
                }
            }

            // 드래그 종료: 좌클릭 해제
            if (!leftButton.isPressed)
            {
                _isDraggingMinimap = false;
                return;
            }

            // 드래그 중이 아니면 무시
            if (!_isDraggingMinimap) return;

            // 현재 마우스 위치 → UV → 월드 좌표 → 카메라 이동
            Vector2 screenPos = mouse.position.ReadValue();
            var rectTransform = minimapImage.rectTransform;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPos, null, out var localPoint))
                return;

            var rect = rectTransform.rect;
            float u = Mathf.Clamp01((localPoint.x - rect.x) / rect.width);
            float v = Mathf.Clamp01((localPoint.y - rect.y) / rect.height);

            float2 mapSize = _mapMax - _mapMin;
            float worldX = _mapMin.x + u * mapSize.x;
            float worldZ = _mapMin.y + v * mapSize.y;

            var cam = Camera.main;
            if (cam == null) return;

            var pos = cam.transform.position;
            cam.transform.position = new Vector3(worldX, pos.y, worldZ);
        }

        private void SwitchToEdgePanIfNeeded()
        {
            if (_cameraStateQuery == null || _cameraStateQuery.IsEmpty) return;

            var em = _clientWorld.EntityManager;
            var entity = _cameraStateQuery.GetSingletonEntity();
            var state = em.GetComponentData<CameraState>(entity);

            if (state.CurrentMode == CameraMode.HeroFollow)
            {
                state.CurrentMode = CameraMode.EdgePan;
                state.TargetEntity = Entity.Null;
                em.SetComponentData(entity, state);
            }
        }

        private void DrawEntities(EntityQuery query, Color color, int dotSize)
        {
            if (query == null || query.IsEmpty) return;

            var em = _clientWorld.EntityManager;
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var transform = em.GetComponentData<LocalTransform>(entities[i]);
                DrawDot(new float2(transform.Position.x, transform.Position.z), color, dotSize);
            }
            entities.Dispose();
        }

        private void DrawDot(float2 worldPos, Color color, int size)
        {
            float2 mapSize = _mapMax - _mapMin;
            if (mapSize.x <= 0 || mapSize.y <= 0) return;

            float u = (worldPos.x - _mapMin.x) / mapSize.x;
            float v = (worldPos.y - _mapMin.y) / mapSize.y;

            int cx = (int)(u * textureSize);
            int cy = (int)(v * textureSize);

            var c32 = (Color32)color;
            int half = size / 2;

            for (int dy = -half; dy <= half; dy++)
            {
                for (int dx = -half; dx <= half; dx++)
                {
                    int px = cx + dx;
                    int py = cy + dy;
                    if (px >= 0 && px < textureSize && py >= 0 && py < textureSize)
                    {
                        _pixels[py * textureSize + px] = c32;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
