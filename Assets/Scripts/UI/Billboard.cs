using UnityEngine;

public class Billboard : MonoBehaviour
{
    Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
    }

    void LateUpdate()
    {
        if (mainCam == null) return;

        // 카메라를 정면으로 바라보게 함
        transform.forward = mainCam.transform.forward;
    }
}
