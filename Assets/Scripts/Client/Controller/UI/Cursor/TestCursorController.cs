using UnityEngine;

// 런타임에 임시 원형(링) 커서를 만들어 적용하는 테스트용 스크립트
public class TestCursorController : MonoBehaviour
{
    [Header("Cursor Settings")]
    [SerializeField] private int size = 32;        // 커서 텍스처 한 변 픽셀 크기
    [SerializeField] private int thickness = 2;    // 링 두께(픽셀)

    private Texture2D runtimeCursor;               // 런타임에 생성한 커서 텍스처 (종료 시 Destroy 필요)

    private void Awake()
    {
        // 링 모양 텍스처를 런타임에 생성한다.
        runtimeCursor = CreateRingCursor(size, thickness);

        // 시작 시 커서를 즉시 적용한다.
        ApplyCursor();
    }

    private void OnEnable()
    {
        // 오브젝트가 다시 활성화될 때도 커서를 재적용한다.
        ApplyCursor();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // 창/게임뷰 포커스가 돌아올 때 Unity가 커서를 기본값으로 되돌리는 경우가 있어
        // 포커스를 얻으면 다시 적용해준다.
        if (hasFocus)
            ApplyCursor();
    }

    private void ApplyCursor()
    {
        // 시스템 커서를 보이게 하고 잠금을 해제한다.
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // 커서 클릭 기준점(Hotspot)을 텍스처 중앙으로 둔다.
        Vector2 hotspot = new Vector2(size * 0.5f, size * 0.5f);

        // OS 커서를 우리가 만든 텍스처로 교체한다.
        Cursor.SetCursor(runtimeCursor, hotspot, CursorMode.Auto);
    }

    private void OnDestroy()
    {
        // 런타임에 생성한 텍스처는 직접 파괴해서 메모리 누수를 막는다.
        if (runtimeCursor != null)
        {
            Destroy(runtimeCursor);
            runtimeCursor = null;
        }
    }

    private static Texture2D CreateRingCursor(int size, int thickness)
    {
        // 투명 배경 RGBA 텍스처를 만든다(커서용).
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // 커서는 가장자리 반복이 필요 없으므로 Clamp.
        tex.wrapMode = TextureWrapMode.Clamp;

        // 픽셀 느낌(선명한 도형)으로 보이게 Point 필터.
        tex.filterMode = FilterMode.Point;

        // 투명/흰색 픽셀 색상 정의.
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color white = new Color(1f, 1f, 1f, 1f);

        // 중심 좌표.
        int cx = size / 2;
        int cy = size / 2;

        // 바깥 반지름 / 안쪽 반지름(두께만큼 뺀 값).
        float outerR = (size * 0.5f) - 1f;
        float innerR = Mathf.Max(outerR - thickness, 0f);

        // 거리 비교를 sqrt 없이 하려고 제곱값으로 둔다.
        float outerRSq = outerR * outerR;
        float innerRSq = innerR * innerR;

        // 모든 픽셀을 돌면서 링 영역이면 흰색, 아니면 투명으로 찍는다.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dSq = (dx * dx) + (dy * dy);

                bool inRing = (dSq <= outerRSq) && (dSq >= innerRSq);
                tex.SetPixel(x, y, inRing ? white : clear);
            }
        }

        // 픽셀 변경을 GPU에 반영한다.
        tex.Apply();
        return tex;
    }
}
