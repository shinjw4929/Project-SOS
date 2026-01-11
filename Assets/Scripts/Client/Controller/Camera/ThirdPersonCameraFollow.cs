using UnityEngine;

// 3인칭 메인 카메라가 타겟(히어로)을 부드럽게 따라가도록 하는 컴포넌트
// - 카메라 회전은 건드리지 않고, 위치만 타겟 + 오프셋으로 따라간다.
// - LateUpdate에서 처리해서, 타겟 이동이 끝난 뒤(프레임 마지막)에 카메라가 따라가도록 한다.
public class ThirdPersonCameraFollow : MonoBehaviour
{
    // 따라갈 대상(히어로 Transform)
    [SerializeField] private Transform target;

    // 타겟 위치 기준으로 카메라가 유지할 상대 위치(오프셋)
    // 예: (0, 80, -10) 이면 위쪽에서 약간 뒤로 떨어져 보게 된다.
    [Header("Keep current angle, only follow position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 80f, -10f);

    // 부드럽게 따라가는 정도(값이 작을수록 더 즉각적으로 따라감)
    [SerializeField] private float smoothTime = 0.12f;

    // SmoothDamp 내부에서 사용하는 속도 누적값
    private Vector3 velocity;

    // 외부에서 타겟을 지정한다.
    // 최초 1회는 위치를 바로 맞춰서 시작 순간에 튀는 현상을 줄인다.
    public void SetTarget(Transform t)
    {
        target = t;
        if (target != null)
            transform.position = target.position + offset;
    }

    // 타겟의 이동(또는 ECS/네트워크 반영)이 끝난 뒤 카메라가 최종 위치로 따라가도록 LateUpdate를 사용한다.
    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
    }
}
