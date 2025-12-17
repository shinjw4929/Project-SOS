using UnityEngine;

public class GridManager : MonoBehaviour
{
    // 어디서든 접근 가능한 싱글톤
    public static GridManager Instance { get; private set; }

    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 1.0f; // 그리드 한 칸 크기
    [SerializeField] private bool showDebugGrid = true; // 기즈모 보이기 여부

    private void Awake()
    {
        // 싱글톤 초기화
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // (선택) 씬이 넘어가도 파괴되지 않게 한다.
        // DontDestroyOnLoad(gameObject);
    }

    // [핵심 1] 월드 좌표 -> 그리드 중심 좌표 변환 (스냅핑)
    // ObjectSpawner, PreviewSystem, Cursor 등이 모두 이걸 갖다 씀
    public Vector3 GetSnapPosition(Vector3 worldPosition)
    {
        float x = Mathf.Round(worldPosition.x / cellSize) * cellSize;
        float z = Mathf.Round(worldPosition.z / cellSize) * cellSize;
        return new Vector3(x, 0, z); // Y는 0 고정 (필요 시 worldPosition.y)
    }

    // [핵심 2] 외부에서 그리드 크기를 알고 싶을 때
    public float CellSize => cellSize;

    // [디버깅] 씬 뷰에 전체 그리드 그려주기
    private void OnDrawGizmos()
    {
        if (!showDebugGrid) return;

        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        // 내 위치 중심으로 20x20 정도만 그려봄 (예시)
        for (float x = -10; x <= 10; x += cellSize)
        {
            Gizmos.DrawLine(new Vector3(x, 0, -10), new Vector3(x, 0, 10));
        }
        for (float z = -10; z <= 10; z += cellSize)
        {
            Gizmos.DrawLine(new Vector3(-10, 0, z), new Vector3(10, 0, z));
        }
    }
}