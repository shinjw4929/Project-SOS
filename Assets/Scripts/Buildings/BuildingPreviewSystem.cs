using UnityEngine;

public class BuildingPreviewSystem : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material validMaterial;   // 초록색 반투명
    [SerializeField] private Material invalidMaterial; // 빨간색 반투명

    private GameObject previewObject; // 현재 보여주고 있는 유령 객체
    private MeshRenderer[] renderers; // 색상을 바꾸기 위한 렌더러들
    private float yOffset = 0f;

    // 미리보기 객체 생성 (건설 모드 진입 시 호출)
    public void ShowPreview(GameObject prefab, Vector3 position)
    {
        if (previewObject != null) Destroy(previewObject);

        // 1. 프리팹을 복제해서 미리보기 객체 생성
        previewObject = Instantiate(prefab, position, Quaternion.identity);
        yOffset = prefab.transform.position.y;

        // 2. 물리 충돌체(Collider) 제거 (플레이어를 밀어내거나 감지되면 안 되므로)
        Collider[] colliders = previewObject.GetComponentsInChildren<Collider>();
        foreach (var col in colliders) Destroy(col);

        // 3. 렌더러 캐싱 (색상 변경용)
        renderers = previewObject.GetComponentsInChildren<MeshRenderer>();

        // 4. 그림자 끄기 (선택사항)
        foreach (var r in renderers)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // 미리보기 객체 끄기 (건설 모드 종료 시 호출)
    public void HidePreview()
    {
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
        }
    }

    // 위치와 색상 업데이트 (매 프레임 호출)
    public void UpdatePreview(Vector3 position, bool isValid)
    {
        if (previewObject == null) return;

        // 위치 이동
        previewObject.transform.position = position + new Vector3(0, yOffset, 0);

        // 색상 변경 (가능하면 초록, 불가능하면 빨강)
        Material targetMat = isValid ? validMaterial : invalidMaterial;

        foreach (var r in renderers)
        {
            // 모든 재질을 교체 (배열 전체를 덮어씌움)
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
            r.materials = mats;
        }
    }
}