using UnityEngine;
using UnityEngine.InputSystem;
using System; // Action 사용을 위해 필요

public enum PlayerState
{
    Idle,
    Moving,
    MovingToBuild, // 건설 위치로 이동 중인 상태
    Building,
    Dead,
    Reviving
}

public class PlayerController : MonoBehaviour
{
    private CharacterController charController;
    private Movement3D movement3D;
    private GameObject targetPrefab;

    [Header("State")]
    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;

    [Header("Input Settings")]
    [SerializeField] private InputActionReference rightClickInput;
    [SerializeField] private InputActionReference mousePosInput;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask groundLayer;

    private Camera mainCamera;
    private Vector3 targetPosition;
    private float stopDistance = 0.5f; // 기본 이동 정지 거리

    // 건설 예약을 위한 변수들
    private Action onArrivedCallback; // 도착 시 실행할 함수(건설)
    private float interactionRange = 0f; // 건설 사거리

    // 끼임 감지(Stuck Detection) 변수
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private const float STUCK_THRESHOLD = 0.5f; // 0.5초 동안 제자리니?

    private void Awake()
    {
        mainCamera = Camera.main;
        movement3D = GetComponent<Movement3D>();
        charController = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        if (rightClickInput != null) { rightClickInput.action.Enable(); rightClickInput.action.performed += OnRightClick; }
        if (mousePosInput != null) mousePosInput.action.Enable();
    }

    private void OnDisable()
    {
        if (rightClickInput != null) { rightClickInput.action.performed -= OnRightClick; rightClickInput.action.Disable(); }
        if (mousePosInput != null) mousePosInput.action.Disable();
    }

    private void Update()
    {
        if (CurrentState == PlayerState.Dead) return;


        switch (CurrentState)
        {
            case PlayerState.Idle:
                break;
            case PlayerState.Moving:
                HandleMovement();
                break;
            case PlayerState.MovingToBuild:
                HandleMovementToBuild(); // 건설 이동 (사거리 체크 포함)
                break;
            case PlayerState.Building:
                break;
            case PlayerState.Reviving:
                break;
            default:
                break;
        }

    }

    // [핵심 기능 1] 외부(BuildController)에서 "여기 가서 이거 해!"라고 명령하는 함수
    public void MoveToInteract(Vector3 targetPos, GameObject prefab, float range, Action onArrived)
    {
        targetPosition = new Vector3(targetPos.x, transform.position.y, targetPos.z);
        interactionRange = range;
        onArrivedCallback = onArrived;

        targetPrefab = prefab;

        // 끼임 감지 초기화
        lastPosition = transform.position;
        stuckTimer = 0f;

        CurrentState = PlayerState.MovingToBuild;
    }

    // [핵심 기능 2] 건설 위치로 이동하는 로직 (사거리 안에 들면 멈춤)
    private void HandleMovementToBuild()
    {
        // 1. 높이(Y) 무시하고 평면 좌표만 사용
        Vector3 myPosFlat = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 targetPosFlat = new Vector3(targetPosition.x, 0, targetPosition.z);

        // ---------------------------------------------------------
        // [더 쉬운 방법] 가상의 박스(Bounds)를 만들어서 거리 측정
        // ---------------------------------------------------------
        float distanceToBuilding = 0f;

        if (targetPrefab != null)
        {
            // A. 프리팹의 사이즈(크기)를 가져옵니다.
            // (주의: 프리팹에 Collider가 있어야 합니다)
            Vector3 size = targetPrefab.GetComponent<Collider>().bounds.size;

            // B. 목표 지점에 가상의 박스(Bounds)를 생성합니다.
            Bounds virtualBounds = new Bounds(targetPosFlat, size);

            // C. "내 위치에서 이 박스의 가장 가까운 지점"을 찾습니다. (Unity 내장 함수)
            Vector3 closestPoint = virtualBounds.ClosestPoint(myPosFlat);

            // D. 그 지점까지의 거리 측정
            distanceToBuilding = Vector3.Distance(myPosFlat, closestPoint);
        }
        else
        {
            // 프리팹 정보가 없으면 그냥 중심점 거리 사용
            distanceToBuilding = Vector3.Distance(myPosFlat, targetPosFlat);
        }

        // 2. 내 몸통 두께(반지름)만큼 더 빼줌 (최종 거리)
        // 결과가 0보다 작으면 이미 닿은 것
        float finalDistance = Mathf.Max(0, distanceToBuilding - charController.radius); //의 radius 개념 활용

        // ---------------------------------------------------------

        // 3. 사거리 체크 및 이동
        if (finalDistance <= interactionRange)
        {
            StopMoving();
            onArrivedCallback?.Invoke();
            onArrivedCallback = null;
            targetPrefab = null;
        }
        else
        {
            Vector3 direction = (targetPosition - transform.position).normalized;
            movement3D.MoveDirection = direction;
            CheckIfStuck();
        }
    }

    // [핵심 기능 3] 가로막힘(장애물 끼임) 해결 로직
    private void CheckIfStuck()
    {
        // 내 위치가 거의 안 변했는지 확인 (0.01f는 아주 미세한 움직임)
        if (Vector3.Distance(transform.position, lastPosition) < 0.01f * Time.deltaTime * 60f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > STUCK_THRESHOLD)
            {
                Debug.Log("이동 불가! 장애물에 막혀 건설이 취소되었습니다.");
                StopMoving(); // 이동 취소
                onArrivedCallback = null; // 예약된 건설도 취소
            }
        }
        else
        {
            // 잘 움직이고 있으면 타이머 리셋
            stuckTimer = 0f;
            lastPosition = transform.position;
        }
    }

    // 기존 일반 이동 로직
    private void HandleMovement()
    {
        Vector3 myPos = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 targetPos = new Vector3(targetPosition.x, 0, targetPosition.z);

        if (Vector3.Distance(myPos, targetPos) <= stopDistance)
        {
            StopMoving();
        }
        else
        {
            Vector3 direction = (targetPosition - transform.position).normalized;
            movement3D.MoveDirection = direction;
        }
    }

    // 우클릭 이동 시
    private void OnRightClick(InputAction.CallbackContext context)
    {
        Vector2 mouseScreenPos = mousePosInput.action.ReadValue<Vector2>();
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
        {
            // 이동 명령이 들어오면 기존 건설 예약은 취소
            onArrivedCallback = null;
            SetTarget(hit.point);
        }
    }

    private void SetTarget(Vector3 target)
    {
        targetPosition = new Vector3(target.x, transform.position.y, target.z);
        CurrentState = PlayerState.Moving;
    }

    private void StopMoving()
    {
        CurrentState = PlayerState.Idle;
        movement3D.MoveDirection = Vector3.zero;
    }
}