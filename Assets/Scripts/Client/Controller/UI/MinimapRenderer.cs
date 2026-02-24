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
    /// 적+다른 유저 유닛: MinimapDataState(RPC, teamId 기반 분기), 자기 유닛/건물: Ghost 엔티티 직접 쿼리.
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
        private EntityQuery _localTeamQuery;

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

                    // 자기 유닛만 Ghost 쿼리 (GhostOwnerIsLocal 필터)
                    _unitQuery = em.CreateEntityQuery(
                        typeof(UnitTag),
                        typeof(LocalTransform),
                        typeof(GhostInstance),
                        typeof(GhostOwnerIsLocal));

                    _structureQuery = em.CreateEntityQuery(
                        typeof(StructureTag),
                        typeof(LocalTransform));

                    // 자기 히어로만 표시
                    _heroQuery = em.CreateEntityQuery(
                        typeof(HeroTag),
                        typeof(LocalTransform),
                        typeof(GhostOwnerIsLocal));

                    _resourceQuery = em.CreateEntityQuery(
                        typeof(ResourceNodeTag),
                        typeof(LocalTransform));

                    _cameraSettingsQuery = em.CreateEntityQuery(typeof(CameraSettings));
                    _cameraStateQuery = em.CreateEntityQuery(typeof(CameraState));
                    _userStateQuery = em.CreateEntityQuery(typeof(UserState));

                    _localTeamQuery = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HeroTag>(),
                        ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                        ComponentType.ReadOnly<Team>());

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

            // 적 + 다른 유저 유닛 (MinimapDataState RPC 데이터, teamId 기반 분기)
            DrawRpcEntities();

            // 자원 노드
            DrawEntities(_resourceQuery, resourceColor, 2);

            // 자기 유닛 (Ghost 쿼리 - GhostOwnerIsLocal)
            DrawTeamEntities(_unitQuery, 2);

            // 건물
            DrawTeamEntities(_structureQuery, 3);

            // 히어로 (마지막 = 최상위)
            DrawTeamEntities(_heroQuery, 4);

            // 카메라 뷰포트 사각형 (최상위 레이어)
            DrawCameraViewport();

            _texture.SetPixels32(_pixels);
            _texture.Apply();
        }

        private void DrawRpcEntities()
        {
            if (_minimapDataQuery == null || _minimapDataQuery.IsEmpty) return;

            var state = _minimapDataQuery.GetSingleton<MinimapDataState>();
            if (!state.Data.IsCreated || state.Data.Length == 0) return;

            int localTeamId = GetLocalTeamId();

            var data = state.Data;
            for (int i = 0; i < data.Length; i++)
            {
                var entry = data[i];
                int teamId = (int)entry.z;

                // 자기 유닛은 Ghost 쿼리로 표시
                if (teamId == localTeamId) continue;

                if (teamId == -1)
                {
                    DrawDot(new float2(entry.x, entry.y), enemyColor, 1);
                }
                else
                {
                    var teamColor = TeamColorPalette.GetTeamColor(teamId);
                    DrawDot(new float2(entry.x, entry.y),
                        new Color(teamColor.x, teamColor.y, teamColor.z, teamColor.w), 2);
                }
            }
        }

        private int GetLocalTeamId()
        {
            if (_localTeamQuery == null || _localTeamQuery.IsEmpty) return -99;

            var em = _clientWorld.EntityManager;
            var entities = _localTeamQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            int teamId = -99;
            if (entities.Length > 0)
                teamId = em.GetComponentData<Team>(entities[0]).teamId;
            entities.Dispose();
            return teamId;
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

            float uMin = (camPos.x - viewHalf.x - _mapMin.x) / mapSize.x;
            float uMax = (camPos.x + viewHalf.x - _mapMin.x) / mapSize.x;
            float vMin = (camPos.z - viewHalf.y - _mapMin.y) / mapSize.y;
            float vMax = (camPos.z + viewHalf.y - _mapMin.y) / mapSize.y;

            int x0 = Mathf.Clamp((int)(uMin * textureSize), 0, textureSize - 1);
            int x1 = Mathf.Clamp((int)(uMax * textureSize), 0, textureSize - 1);
            int y0 = Mathf.Clamp((int)(vMin * textureSize), 0, textureSize - 1);
            int y1 = Mathf.Clamp((int)(vMax * textureSize), 0, textureSize - 1);

            var c32 = (Color32)viewportColor;

            DrawHLine(x0, x1, y0, c32, viewportLineWidth);
            DrawHLine(x0, x1, y1, c32, viewportLineWidth);
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

            if (leftButton.wasPressedThisFrame)
            {
                Vector2 pressPos = mouse.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(minimapImage.rectTransform, pressPos))
                {
                    _isDraggingMinimap = true;
                    SwitchToEdgePanIfNeeded();
                }
            }

            if (!leftButton.isPressed)
            {
                _isDraggingMinimap = false;
                return;
            }

            if (!_isDraggingMinimap) return;

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
            var camState = em.GetComponentData<CameraState>(entity);

            if (camState.CurrentMode == CameraMode.HeroFollow)
            {
                camState.CurrentMode = CameraMode.EdgePan;
                camState.TargetEntity = Entity.Null;
                em.SetComponentData(entity, camState);
            }
        }

        private void DrawTeamEntities(EntityQuery query, int dotSize)
        {
            if (query == null || query.IsEmpty) return;

            var em = _clientWorld.EntityManager;
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var transform = em.GetComponentData<LocalTransform>(entities[i]);
                var team = em.GetComponentData<Team>(entities[i]);
                var teamColor = TeamColorPalette.GetTeamColor(team.teamId);
                DrawDot(new float2(transform.Position.x, transform.Position.z), new Color(teamColor.x, teamColor.y, teamColor.z, teamColor.w), dotSize);
            }
            entities.Dispose();
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
