using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Movement3D movement3D;
    private Camera mainCamera;

    [Header("Input Settings")]
    [SerializeField] private InputActionReference rightClickInput;
    [SerializeField] private InputActionReference mousePosInput;

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask groundLayer;

    private Vector3 targetPosition;
    private bool isMoving = false;

    // [수정] 멈추는 거리를 조금 여유있게 줍니다 (0.1f -> 0.2f 또는 0.5f)
    private const float StopDistance = 0.2f;

    private void Awake()
    {
        mainCamera = Camera.main;
        if (movement3D == null) movement3D = GetComponent<Movement3D>();
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
        if (isMoving)
        {
            // Y축 무시하고 수평 거리만 계산 (덜덜거림 방지 핵심)
            Vector3 myPos = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 targetPos = new Vector3(targetPosition.x, 0, targetPosition.z);

            float distance = Vector3.Distance(myPos, targetPos);

            // [수정] 도착 판정 로직 개선
            if (distance <= StopDistance)
            {
                StopMoving(); // 그냥 멈춤 (강제 위치 이동 삭제)
            }
            else
            {
                Vector3 direction = (targetPosition - transform.position).normalized;
                movement3D.MoveDirection = direction;
            }
        }
    }

    private void OnRightClick(InputAction.CallbackContext context)
    {
        Vector2 mouseScreenPos = mousePosInput.action.ReadValue<Vector2>();
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
        {
            SetTarget(hit.point);
        }
    }

    private void SetTarget(Vector3 target)
    {
        // Y축은 현재 내 높이 유지
        targetPosition = new Vector3(target.x, transform.position.y, target.z);
        isMoving = true;
    }

    private void StopMoving()
    {
        isMoving = false;
        movement3D.MoveDirection = Vector3.zero;
    }
}