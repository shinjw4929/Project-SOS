using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// 그리드 점유 상태를 시각적으로 표시하는 디버그 도구.
    /// 씬에 배치 후 Inspector에서 활성화하면 점유된 셀에 색상 쿼드를 표시한다.
    /// 게임 로직에 영향 없음 (읽기 전용).
    /// </summary>
    public class GridOccupancyDebugView : MonoBehaviour
    {
        [Header("표시 옵션")]
        [Tooltip("건물 점유(IsOccupied) 셀 표시")]
        [SerializeField] private bool showOccupied = true;
        [Tooltip("경로 차단(IsPathBlocked) 셀 표시")]
        [SerializeField] private bool showPathBlocked;

        [Header("색상")]
        [SerializeField] private Color occupiedColor = new Color(1f, 0f, 0f, 0.45f);
        [SerializeField] private Color pathBlockedColor = new Color(1f, 0.5f, 0f, 0.45f);

        [Header("렌더링")]
        [Tooltip("쿼드 표시 높이 (Y축)")]
        [SerializeField] private float displayHeight = 0.15f;
        [Tooltip("갱신 주기 (초)")]
        [SerializeField] private float refreshInterval = 0.25f;

        private Mesh _quadMesh;
        private Material _occupiedMaterial;
        private Material _pathBlockedMaterial;

        private const int BatchSize = 1023;
        private Matrix4x4[] _occupiedMatrices;
        private Matrix4x4[] _pathBlockedMatrices;
        private int _occupiedCount;
        private int _pathBlockedCount;

        private float _lastRefreshTime;
        private World _clientWorld;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        private void OnEnable()
        {
            _quadMesh = CreateQuadMesh();
            _occupiedMaterial = CreateMaterial(occupiedColor);
            _pathBlockedMaterial = CreateMaterial(pathBlockedColor);
            _occupiedMatrices = new Matrix4x4[BatchSize];
            _pathBlockedMatrices = new Matrix4x4[BatchSize];
            _lastRefreshTime = -refreshInterval;
        }

        private void OnDisable()
        {
            _occupiedCount = 0;
            _pathBlockedCount = 0;
            if (_occupiedMaterial != null) Destroy(_occupiedMaterial);
            if (_pathBlockedMaterial != null) Destroy(_pathBlockedMaterial);
            if (_quadMesh != null) Destroy(_quadMesh);
        }

        private void Update()
        {
            if (Time.time - _lastRefreshTime >= refreshInterval)
            {
                _lastRefreshTime = Time.time;
                Rebuild();
            }

            DrawBatched(_occupiedMatrices, _occupiedCount, _occupiedMaterial, showOccupied);
            DrawBatched(_pathBlockedMatrices, _pathBlockedCount, _pathBlockedMaterial, showPathBlocked);
        }

        private void Rebuild()
        {
            if (_clientWorld == null || !_clientWorld.IsCreated)
            {
                _clientWorld = FindClientWorld();
                if (_clientWorld == null)
                {
                    _occupiedCount = 0;
                    _pathBlockedCount = 0;
                    return;
                }
            }

            var em = _clientWorld.EntityManager;
            using var query = em.CreateEntityQuery(typeof(GridSettings));
            if (query.IsEmpty)
            {
                _occupiedCount = 0;
                _pathBlockedCount = 0;
                return;
            }

            var gridEntity = query.GetSingletonEntity();
            var settings = em.GetComponentData<GridSettings>(gridEntity);

            if (!em.HasBuffer<GridCell>(gridEntity))
            {
                _occupiedCount = 0;
                _pathBlockedCount = 0;
                return;
            }

            var cells = em.GetBuffer<GridCell>(gridEntity, true);
            int sizeX = settings.GridSize.x;
            int sizeZ = settings.GridSize.y;
            float cellSize = settings.CellSize;

            int occCount = 0;
            int blkCount = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].IsOccupied != 0) occCount++;
                if (cells[i].IsPathBlocked != 0) blkCount++;
            }

            if (_occupiedMatrices.Length < occCount)
                _occupiedMatrices = new Matrix4x4[occCount];
            if (_pathBlockedMatrices.Length < blkCount)
                _pathBlockedMatrices = new Matrix4x4[blkCount];

            var rotation = Quaternion.Euler(90f, 0f, 0f);
            var scale = new Vector3(cellSize, cellSize, 1f);
            int oi = 0;
            int bi = 0;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int idx = z * sizeX + x;
                    if (idx >= cells.Length) break;

                    var cell = cells[idx];
                    float wx = settings.GridOrigin.x + (x + 0.5f) * cellSize;
                    float wz = settings.GridOrigin.y + (z + 0.5f) * cellSize;

                    if (cell.IsOccupied != 0)
                    {
                        _occupiedMatrices[oi++] = Matrix4x4.TRS(
                            new Vector3(wx, displayHeight, wz), rotation, scale);
                    }

                    if (cell.IsPathBlocked != 0)
                    {
                        _pathBlockedMatrices[bi++] = Matrix4x4.TRS(
                            new Vector3(wx, displayHeight + 0.01f, wz), rotation, scale);
                    }
                }
            }

            _occupiedCount = oi;
            _pathBlockedCount = bi;
        }

        private void DrawBatched(Matrix4x4[] matrices, int count, Material material, bool show)
        {
            if (!show || count == 0 || material == null) return;

            for (int offset = 0; offset < count; offset += BatchSize)
            {
                int batchCount = Mathf.Min(BatchSize, count - offset);
                if (offset == 0)
                {
                    Graphics.DrawMeshInstanced(_quadMesh, 0, material, matrices, batchCount);
                }
                else
                {
                    var batch = new Matrix4x4[batchCount];
                    System.Array.Copy(matrices, offset, batch, 0, batchCount);
                    Graphics.DrawMeshInstanced(_quadMesh, 0, material, batch, batchCount);
                }
            }
        }

        private static World FindClientWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient()) return world;
            }
            return null;
        }

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "DebugQuad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
                normals = new[]
                {
                    Vector3.back, Vector3.back, Vector3.back, Vector3.back
                }
            };
            return mesh;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            var mat = new Material(shader);
            mat.enableInstancing = true;
            mat.SetColor(ColorProperty, color);
            mat.SetColor(BaseColorProperty, color);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.renderQueue = 3000;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            return mat;
        }
    }
}
