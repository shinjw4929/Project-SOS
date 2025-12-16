using UnityEngine;
using UnityEngine.InputSystem; // Input System 네임스페이스 필수

public class PlayerController : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Movement2D movement2D; // 이동 모터 연결
    private Camera mainCamera;

    [Header("Input Settings")]
    // 1. 클릭 액션 (Button Type) - 예: "RightClick"
    [SerializeField] private InputActionReference rightClickInput;

    // 2. 좌표 액션 (Value Type, Vector2) - 예: "MousePos"
    // 새로 만드신 액션을 여기에 연결하세요.
    [SerializeField] private InputActionReference mousePosInput;

    // 이동 관련 변수들
    private Vector3 targetPosition;
    private bool isMoving = false;
    private const float StopDistance = 0.1f; // 목표 도달 인정 거리

    private void Awake()
    {
        mainCamera = Camera.main;

        // Movement2D가 연결되지 않았을 경우 자동 할당
        if (movement2D == null)
            movement2D = GetComponent<Movement2D>();
    }

    private void OnEnable()
    {
        // 1. 우클릭 액션 활성화 및 이벤트 연결
        if (rightClickInput != null && rightClickInput.action != null)
        {
            rightClickInput.action.Enable();
            rightClickInput.action.performed += OnRightClick;
        }

        // 2. 마우스 좌표 액션 활성화 (이걸 켜야 값을 읽을 수 있습니다!)
        if (mousePosInput != null && mousePosInput.action != null)
        {
            mousePosInput.action.Enable();
        }
    }

    private void OnDisable()
    {
        // 1. 우클릭 액션 비활성화 및 이벤트 해제
        if (rightClickInput != null && rightClickInput.action != null)
        {
            rightClickInput.action.performed -= OnRightClick;
            rightClickInput.action.Disable();
        }

        // 2. 마우스 좌표 액션 비활성화
        if (mousePosInput != null && mousePosInput.action != null)
        {
            mousePosInput.action.Disable();
        }
    }

    private void Update()
    {
        if (isMoving)
        {
            // 1. 현재 나와 목표 사이의 거리 계산
            float distance = Vector3.Distance(transform.position, targetPosition);

            // 2. [핵심] 이번 프레임에 내가 이동할 수 있는 최대 거리 계산
            // (Movement2D의 속도 정보를 가져와서 계산)
            float step = movement2D.moveSpeed * Time.deltaTime;

            // 3. 만약 남은 거리가 내가 이동할 거리보다 작다면? (도착으로 간주!)
            if (distance <= step)
            {
                // [스냅] 목표 위치에 강제로 딱! 붙여버립니다. (오차 0)
                transform.position = targetPosition;

                // 이동 종료
                StopMoving();
            }
            else
            {
                // 아직 멀었으면 계속 방향 갱신해서 이동
                Vector3 direction = (targetPosition - transform.position).normalized;
                movement2D.MoveDirection = direction;
            }
        }
    }

    // 우클릭 발생 시 실행되는 함수
    private void OnRightClick(InputAction.CallbackContext context)
    {
        // [핵심 변경] 하드웨어(Mouse.current) 대신 인풋 시스템 액션에서 좌표 읽기
        // 연결된 'MousePos' 액션으로부터 Vector2 값을 가져옵니다.
        Vector2 mouseScreenPos = mousePosInput.action.ReadValue<Vector2>();

        // 화면 좌표 -> 월드 좌표 변환
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        worldPos.z = 0; // 2D 게임이므로 Z축 0으로 고정

        SetTarget(worldPos);
    }

    // 목표 지점 설정
    private void SetTarget(Vector3 target)
    {
        targetPosition = target;
        isMoving = true;
    }

    // 목표 도달 확인 로직
    private void CheckDestination()
    {
        float distance = Vector3.Distance(transform.position, targetPosition);

        // 목표 지점과의 거리가 설정된 오차범위보다 작으면 도착으로 간주
        if (distance <= StopDistance)
        {
            StopMoving();
        }
    }

    // 이동 정지 처리
    private void StopMoving()
    {
        isMoving = false;
        movement2D.MoveDirection = Vector3.zero; // 이동 멈춤

        // (선택사항) 정확한 위치에 딱 맞추고 싶다면 아래 주석 해제
        // transform.position = targetPosition; 
    }
}