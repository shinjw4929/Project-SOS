using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (!cam && Camera.allCamerasCount > 0) // Unity Object는 implicit bool 사용
            cam = Camera.allCameras[0];

        if (!cam) return; // Unity Object는 implicit bool 사용

        // �׻� ī�޶� �������� ����(������ ���� ����)
        transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward,
                         cam.transform.rotation * Vector3.up);
    }
}
