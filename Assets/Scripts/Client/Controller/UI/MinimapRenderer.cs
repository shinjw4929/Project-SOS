using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.NetCode;
using Shared;


namespace Client
{
    /// <summary>
    /// UI 기반 미니맵 렌더러. Texture2D에 적/아군/건물/히어로 위치를 점으로 렌더링.
    /// 적: MinimapDataState(RPC), 아군/건물: Ghost 엔티티 직접 쿼리.
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

        private float2 _mapMin;
        private float2 _mapMax;
        private bool _boundsInitialized;

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
            _timer += Time.deltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;

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

            _texture.SetPixels32(_pixels);
            _texture.Apply();
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
