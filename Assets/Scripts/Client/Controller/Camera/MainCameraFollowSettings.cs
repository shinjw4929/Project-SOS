using UnityEngine;

// 메인 카메라가 히어로를 따라갈 때 사용할 설정 값
public class MainCameraFollowSettings : MonoBehaviour
{
    // 히어로 위치 기준 카메라 오프셋
    public Vector3 offset = new Vector3(0f, 20f, -10f);

    // 따라가는 부드러움(작을수록 더 즉각 반응)
    public float smoothTime = 0.12f;

    // true면 시작 시점 카메라 회전을 고정한다.
    public bool lockRotation = true;
}
