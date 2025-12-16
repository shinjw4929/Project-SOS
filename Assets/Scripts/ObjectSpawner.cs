using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private GameObject prefabToSpawn;
    [SerializeField] private Transform spawnPoint; // (이제 미리보기에서는 마우스 위치를 쓰므로 이건 보조용)

    [Header("Grid Settings")]
    [SerializeField] private bool useGrid = true;
    [SerializeField] public float gridSize = 1.0f; // 외부에서 참조 가능하게 public
    [SerializeField] private LayerMask obstacleLayer;

    // 외부에서 "이 프리팹 좀 빌려줘(미리보기용)" 할 때 사용
    public GameObject PrefabToSpawn => prefabToSpawn;

    // 좌표를 그리드에 맞추는 함수 (Public으로 변경)
    public Vector3 GetGridPosition(Vector3 originalPosition)
    {
        if (!useGrid) return originalPosition;

        float x = Mathf.Round(originalPosition.x / gridSize) * gridSize;
        float z = Mathf.Round(originalPosition.z / gridSize) * gridSize;
        // Y는 0으로 고정하거나 지형 높이에 맞춤
        return new Vector3(x, 0, z);
    }

    // 설치 가능한지 검사하는 함수 (Public으로 변경)
    public bool CanBuild(Vector3 position)
    {
        // 박스 크기를 그리드보다 약간 작게
        Vector3 boxSize = new Vector3(gridSize, 2f, gridSize) * 0.45f;

        // 장애물이 있으면 false, 없으면 true
        bool isBlocked = Physics.CheckBox(position + Vector3.up * 1f, boxSize, Quaternion.identity, obstacleLayer);
        return !isBlocked;
    }

    // 실제 생성 함수 (위치를 받아서 생성)
    public void Spawn(Vector3 position)
    {
        if (prefabToSpawn == null) return;

        // 프리팹의 바닥 보정값을 더해줌
        Vector3 finalPos = position + new Vector3(0, prefabToSpawn.transform.position.y, 0);

        Instantiate(prefabToSpawn, finalPos, Quaternion.identity);
    }
}