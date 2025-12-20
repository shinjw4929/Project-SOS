using UnityEngine;

public class CameraMove : MonoBehaviour
{
    public GameObject Target;

    // 고정 카메라 좌표
    public float offsetX = 0.0f;
    public float offsetY = 0.0f;
    public float offsetZ = -10.0f;

    Vector3 TargetPos;

    // FixedUpdate 대신 LateUpdate 사용 권장
    // 이유: 플레이어가 이동을 다 마친 직후에 카메라가 따라가야 떨림(Jitter)이 없음
    void LateUpdate()
    {
        if (Target == null) return;

        // 1. 목표 위치 계산
        TargetPos = new Vector3(
            Target.transform.position.x + offsetX,
            Target.transform.position.y + offsetY,
            offsetZ // Z축은 고정
        );

        // 2. 그냥 바로 위치를 덮어씌움
        transform.position = TargetPos;
    }
}