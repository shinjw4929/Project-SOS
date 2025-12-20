using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyFollow : MonoBehaviour
{
    public Transform player;
    public float moveSpeed = 3f;

    private CharacterController characterController;

    // 벽에 접촉했는지 여부
    private bool isTouchingWall;

    // 중력 누적값 (CharacterController 보정용)
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (player == null) return;

        // 플레이어 방향 계산 (수평 이동만)
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        dir.Normalize();

        bool grounded = characterController.isGrounded;

        // 바닥에 붙어 있을 때 아래로 미는 힘을 유지해 공중 뜸 방지
        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        // 중력 적용
        verticalVelocity += Physics.gravity.y * Time.deltaTime;

        // 벽에 닿아 있으면 전진하지 않음
        if (isTouchingWall && dir != Vector3.zero)
        {
            isTouchingWall = false;
            return;
        }

        // 이동 벡터 구성
        Vector3 move = dir * moveSpeed;
        move.y = verticalVelocity;

        // CharacterController 이동
        characterController.Move(move * Time.deltaTime);

        // 프레임 종료 시 초기화
        isTouchingWall = false;
    }

    // CharacterController 충돌 콜백
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // 노멀의 y값이 낮으면 벽으로 판단
        if (hit.normal.y < 0.5f)
        {
            isTouchingWall = true;
        }
    }
}
