using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyFollow : MonoBehaviour
{
    public Transform player;
    public float moveSpeed = 3f;

    private CharacterController characterController;

    // Player Movement3D와 동일한 벽 처리용 변수
    private Vector3 lastHitNormal;
    private bool isCollidingWall = false;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (player == null) return;

        // 1. 추적 방향 계산
        Vector3 moveDir = (player.position - transform.position).normalized;

        Vector3 finalMove = moveDir;

        // 2. 벽에 닿아 있다면 벽을 타도록 투영
        if (isCollidingWall)
        {
            finalMove = Vector3.ProjectOnPlane(moveDir, lastHitNormal).normalized;
        }

        finalMove = new Vector3(finalMove.x, 0, finalMove.z);

        // 3. CharacterController 이동 (벽 충돌 정상 작동)
        characterController.Move(finalMove * moveSpeed * Time.deltaTime);

        // 매 프레임 초기화
        isCollidingWall = false;
    }

    // 🔥 Player랑 동일하게 작동
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y < 0.5f)
        {
            isCollidingWall = true;
            lastHitNormal = hit.normal;
        }
    }
}
