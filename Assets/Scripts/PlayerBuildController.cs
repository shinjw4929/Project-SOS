using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBuildController : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionReference toggleBuildModeInput; // 키보드 'B' 등
    [SerializeField] private InputActionReference buildConfirmInput;    // 마우스 'Left Click'
    [SerializeField] private InputActionReference cancelBuildInput;     // 마우스 'Right Click' 또는 'Esc'
    [SerializeField] private InputActionReference mousePosInput;        // 마우스 위치

    [Header("Modules")]
    [SerializeField] private ObjectSpawner objectSpawner;
    [SerializeField] private BuildingPreviewSystem previewSystem;

    [Header("Raycast")]
    [SerializeField] private LayerMask groundLayer;

    private Camera mainCamera;
    private bool isBuildMode = false;
    private Vector3 currentGridPos;
    private bool isCurrentPosValid = false;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void OnEnable()
    {
        toggleBuildModeInput.action.Enable();
        toggleBuildModeInput.action.performed += OnToggleBuildMode;

        buildConfirmInput.action.Enable();
        buildConfirmInput.action.performed += OnBuildConfirm;

        cancelBuildInput.action.Enable();
        cancelBuildInput.action.performed += OnCancelBuild;

        mousePosInput.action.Enable();
    }

    private void OnDisable()
    {
        toggleBuildModeInput.action.performed -= OnToggleBuildMode;
        buildConfirmInput.action.performed -= OnBuildConfirm;
        cancelBuildInput.action.performed -= OnCancelBuild;

        toggleBuildModeInput.action.Disable();
        buildConfirmInput.action.Disable();
        cancelBuildInput.action.Disable();
        mousePosInput.action.Disable();
    }

    private void Update()
    {
        // 건설 모드가 아니면 아무것도 안 함
        if (!isBuildMode) return;

        HandlePreviewUpdate();
    }

    // 마우스 위치를 추적하여 미리보기 업데이트
    private void HandlePreviewUpdate()
    {
        Vector2 mouseScreenPos = mousePosInput.action.ReadValue<Vector2>();
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
        {
            // 1. 마우스 위치를 그리드 좌표로 변환
            currentGridPos = objectSpawner.GetGridPosition(hit.point);

            // 2. 해당 위치에 건설 가능한지 확인
            isCurrentPosValid = objectSpawner.CanBuild(currentGridPos);

            // 3. 미리보기 시스템에 반영 (위치 이동 및 색상 변경)
            previewSystem.UpdatePreview(currentGridPos, isCurrentPosValid);
        }
    }

    // 'B' 키를 눌렀을 때
    private void OnToggleBuildMode(InputAction.CallbackContext context)
    {
        isBuildMode = !isBuildMode;

        if (isBuildMode)
        {
            // 건설 모드 시작: 미리보기 객체 생성
            Debug.Log("건설 모드 ON");
            previewSystem.ShowPreview(objectSpawner.PrefabToSpawn, Vector3.zero);
        }
        else
        {
            // 건설 모드 종료: 미리보기 객체 제거
            Debug.Log("건설 모드 OFF");
            previewSystem.HidePreview();
        }
    }

    // 좌클릭 했을 때 (건설 확정)
    private void OnBuildConfirm(InputAction.CallbackContext context)
    {
        if (!isBuildMode) return;

        // 설치 가능한 위치일 때만 실제 생성
        if (isCurrentPosValid)
        {
            objectSpawner.Spawn(currentGridPos);

            // 건설 후 모드를 끄고 싶으면 아래 주석 해제
            // isBuildMode = false;
            // previewSystem.HidePreview();
        }
        else
        {
            Debug.Log("여기에는 건설할 수 없습니다!");
        }
    }

    // 우클릭/ESC 했을 때 (취소)
    private void OnCancelBuild(InputAction.CallbackContext context)
    {
        if (isBuildMode)
        {
            isBuildMode = false;
            previewSystem.HidePreview();
        }
    }
}