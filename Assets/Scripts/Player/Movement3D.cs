using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class Movement3D : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] public float moveSpeed = 5f;
    [SerializeField] private float rotateSpeed = 5f;

    private CharacterController characterController;
    public Vector3 MoveDirection { get; set; } = Vector3.zero;

    // 벽 충돌 처리를 위한 변수
    private Vector3 lastHitNormal; // 부딪힌 벽의 법선 벡터(반사각 계산용)
    private bool isCollidingWall = false; // 현재 벽에 닿아있는지

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    private void Update()
    {
        MoveCharacter();
        RotateCharacter();

        // 매 프레임 초기화 (충돌이 끝나면 다시 원래대로 가기 위함)
        isCollidingWall = false;
    }

    private void MoveCharacter()
    {
        Vector3 finalMove = MoveDirection;

        // [핵심 로직] 벽에 닿아 있다면? 이동 방향을 벽면을 따라가도록 꺾어줌
        if (isCollidingWall)
        {
            // 원래 가려던 방향(MoveDirection)을 벽의 표면(lastHitNormal)에 투영(Project)함
            // 결과: 벽을 뚫는 힘이 사라지고 벽을 타는 힘만 남음
            finalMove = Vector3.ProjectOnPlane(MoveDirection, lastHitNormal).normalized;
        }

        // Y축(상하) 이동 제거 (수평 이동만)
        finalMove = new Vector3(finalMove.x, 0, finalMove.z);

        // 이동 실행
        characterController.Move(finalMove * moveSpeed * Time.deltaTime);
    }

    private void RotateCharacter()
    {
        if (MoveDirection != Vector3.zero)
        {
            // 회전은 '미끄러지는 방향'이 아니라 '원래 가려던 방향'을 보는 것이 더 자연스러움
            // (만약 벽을 타고 가는 방향을 보게 하고 싶다면 MoveDirection 대신 finalMove를 쓰면 됨)
            Quaternion targetRotation = Quaternion.LookRotation(new Vector3(MoveDirection.x, 0, MoveDirection.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }
    }

    // [유니티 내장 함수] CharacterController가 무언가와 부딪혔을 때 호출됨
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // 1. 바닥이 아닌 경우에만 처리 (바닥의 Normal.y는 보통 1에 가까움)
        // hit.normal.y가 0.5보다 작다면 경사가 가파르거나 벽이라는 뜻
        if (hit.normal.y < 0.5f)
        {
            // 2. 벽이라고 판단됨
            isCollidingWall = true;
            lastHitNormal = hit.normal;
        }
    }
}