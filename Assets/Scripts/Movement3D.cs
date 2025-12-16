using UnityEngine;

public class Movement3D : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] public float moveSpeed = 20f;
    [SerializeField] private float rotateSpeed = 20f; // 회전 속도

    // 외부에서 설정할 이동 방향
    public Vector3 MoveDirection { get; set; } = Vector3.zero;

    private void Update()
    {
        // 1. 이동 (Move)
        transform.position += MoveDirection * moveSpeed * Time.deltaTime;

        // 2. 회전 (Rotate) - 이동하는 방향을 바라보게 함
        if (MoveDirection != Vector3.zero)
        {
            // 목표 회전값 계산 (Y축 기준 회전)
            Quaternion targetRotation = Quaternion.LookRotation(MoveDirection);

            // 부드럽게 회전 (Slerp)
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
        }
    }
}