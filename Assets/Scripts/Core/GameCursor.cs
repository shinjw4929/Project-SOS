using UnityEngine;
using UnityEngine.InputSystem;

public class GameCursor : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionReference mousePosInput;

    private RectTransform rectTransform; // transform 대신 RectTransform 사용
    private Canvas parentCanvas;         // 부모 캔버스 (좌표 변환 기준)
    private Camera mainCamera;

    private void Awake()
    {
        mainCamera = Camera.main;

        // 1. 내 컴포넌트 가져오기
        rectTransform = GetComponent<RectTransform>();

        // 2. 부모 캔버스 찾기 (최상위 캔버스)
        parentCanvas = GetComponentInParent<Canvas>();

        // 3. 커서 숨기기 설정
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
    }

    private void OnEnable()
    {
        mousePosInput.action.Enable();
    }

    private void OnDisable()
    {
        mousePosInput.action.Disable();
        Cursor.visible = true;
    }

    private void Update()
    {
        UpdateCursorPosition();
    }

    private void UpdateCursorPosition()
    {
        // 1. 인풋 시스템에서 화면(스크린) 좌표 가져오기
        Vector2 screenPos = mousePosInput.action.ReadValue<Vector2>();

        // 2. 스크린 좌표를 UI(RectTransform) 내부 좌표로 변환
        // Canvas Render Mode에 따라 카메라 필요 여부가 다름
        Camera cam = null;
        if (parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            cam = mainCamera;
        }

        Vector2 localPoint;
        // 이 함수가 해상도, 캔버스 스케일러 등을 모두 고려하여 정확한 UI 위치를 계산해줍니다.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform, // 기준이 되는 부모 (보통 캔버스)
            screenPos,                               // 마우스 위치
            cam,                                     // 카메라 (Overlay면 null)
            out localPoint                           // 결과가 담길 변수
        );

        // 3. 위치 적용 (UI는 anchoredPosition을 사용)
        rectTransform.anchoredPosition = localPoint;
    }

    // 외부에서 위치를 가져갈 때
    public Vector3 GetPosition()
    {
        // UI 요소의 월드 좌표를 반환하거나, 필요에 따라 anchoredPosition을 반환할 수 있습니다.
        return transform.position;
    }
}