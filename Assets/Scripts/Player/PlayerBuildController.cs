using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBuildController : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionReference toggleBuildModeInput; // 키보드 'Q'
    [SerializeField] private InputActionReference buildConfirmInput;    // 마우스 'Left Click'
    [SerializeField] private InputActionReference cancelBuildInput;     // 키보드 '1'
    [SerializeField] private InputActionReference mousePosInput;        // 마우스 위치

    [Header("Modules")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private BuildingSpawner buildingSpawner;
    [SerializeField] private BuildingPreviewSystem previewSystem;


    [Header("Build Settings")]
    [SerializeField] private float buildRange = 2.0f;

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
            currentGridPos = buildingSpawner.GetGridPosition(hit.point);

            // 2. 해당 위치에 건설 가능한지 확인
            isCurrentPosValid = buildingSpawner.CanBuild(currentGridPos);

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
            previewSystem.ShowPreview(buildingSpawner.PrefabToSpawn, Vector3.zero);
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
        if (!isBuildMode || !isCurrentPosValid) return;

        Vector3 fixedBuildPos = currentGridPos;

        GameObject wantToBuild = buildingSpawner.PrefabToSpawn;

        // 람다 식(Lambda)을 사용하여 도착 시 실행할 코드를 전달함
        playerController.MoveToInteract(fixedBuildPos, wantToBuild, buildRange, () =>
        {

            // 도착했을 때 실행될 코드 (콜백)

            // 도착했는데 그 사이에 누가 자리를 차지했는지 한 번 더 체크
            if (buildingSpawner.CanBuild(fixedBuildPos))
            {
                buildingSpawner.Spawn(fixedBuildPos);

                // 연속 건설을 위해 건설 모드는 끄지 않음 (원하면 끄는 코드 추가)
            }
            else
            {
                Debug.Log("도착했으나 건설 불가능한 상태입니다.");
            }
        });
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