using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Movement3D movement3D; // 3D 모터 연결
    private Camera mainCamera;

    [Header("Input Settings")]
    [SerializeField] private InputActionReference rightClickInput;
    [SerializeField] private InputActionReference mousePosInput;

    [Header("Raycast Settings")]
    [Tooltip("마우스 클릭을 감지할 바닥 레이어 (반드시 설정 필요)")]
    [SerializeField] private LayerMask groundLayer;

    // 이동 관련 변수
    private Vector3 targetPosition;
    private bool isMoving = false;
    private const float StopDistance = 0.1f;

    private void Awake()
    {
        mainCamera = Camera.main;

        if (movement3D == null)
            movement3D = GetComponent<Movement3D>();
    }

    private void OnEnable()
    {
        if (rightClickInput != null)
        {
            rightClickInput.action.Enable();
            rightClickInput.action.performed += OnRightClick;
        }

        if (mousePosInput != null)
        {
            mousePosInput.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (rightClickInput != null)
        {
            rightClickInput.action.performed -= OnRightClick;
            rightClickInput.action.Disable();
        }

        if (mousePosInput != null)
        {
            mousePosInput.action.Disable();
        }
    }

    private void Update()
    {
        if (isMoving)
        {
            // 1. 거리 계산 (Y축 높이 차이는 무시하기 위해 targetPosition의 Y를 내 Y와 맞춤)
            // 하지만 이미 SetTarget에서 Y를 맞췄으므로 그냥 Distance 구해도 됨.
            float distance = Vector3.Distance(transform.position, targetPosition);

            // 2. 이번 프레임 이동 가능 거리
            float step = movement3D.moveSpeed * Time.deltaTime;

            // 3. 도착 판정
            if (distance <= step)
            {
                // 도착: 위치 강제 조정 및 정지
                transform.position = targetPosition;
                StopMoving();
            }
            else
            {
                // 이동 중: 방향 갱신
                Vector3 direction = (targetPosition - transform.position).normalized;
                movement3D.MoveDirection = direction;
            }
        }
    }

    // 우클릭 시 실행 (핵심 변경 부분)
    private void OnRightClick(InputAction.CallbackContext context)
    {
        // 1. 마우스 화면 좌표 가져오기
        Vector2 mouseScreenPos = mousePosInput.action.ReadValue<Vector2>();

        // 2. 화면 좌표 -> 3D 월드로 쏘는 레이(Ray) 생성
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);
        RaycastHit hit;

        // 3. 레이 발사! (Ground 레이어에만 충돌하도록 설정)
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
        {
            // hit.point가 마우스로 찍은 바닥의 월드 좌표입니다.
            SetTarget(hit.point);
        }
    }

    private void SetTarget(Vector3 target)
    {
        // [중요] Y축 고정 로직
        // 클릭한 위치(target)의 X, Z는 가져오되, 
        // 높이(Y)는 현재 플레이어의 높이로 덮어씌웁니다.
        targetPosition = new Vector3(target.x, transform.position.y, target.z);

        isMoving = true;
    }

    private void StopMoving()
    {
        isMoving = false;
        movement3D.MoveDirection = Vector3.zero;
    }
}