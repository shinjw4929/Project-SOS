using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerShoot : MonoBehaviour
{
    /* 투사체 프리팹 */
    public GameObject projectilePrefab;

    /* 발사 위치 (Player 자식 FirePoint) */
    public Transform firePoint;

    /* 메인 카메라 */
    public Camera mainCamera;

    /* Input System - 마우스 커서 위치 */
    [SerializeField] private InputActionReference mousePosInput;

    /* 이동과 동일하게 사용하는 바닥 레이어 */
    [SerializeField] private LayerMask groundLayer;

    private void OnEnable()
    {
        /* 마우스 위치 입력 활성화 */
        if (mousePosInput != null)
            mousePosInput.action.Enable();
    }

    private void OnDisable()
    {
        /* 마우스 위치 입력 비활성화 */
        if (mousePosInput != null)
            mousePosInput.action.Disable();
    }

    private void Update()
    {
        /* F 키를 직접 감지해서 발사 */
        if (Input.GetKeyDown(KeyCode.F))
        {
            Debug.Log("F pressed");
            Shoot();
        }
    }

    private void Shoot()
    {
        /* Input System에서 마우스 화면 좌표 읽기 */
        Vector2 mouseScreenPos = mousePosInput.action.ReadValue<Vector2>();

        /* 카메라에서 마우스 방향으로 Ray 생성 */
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);



        /* 바닥 레이어에 Raycast */
        RaycastHit hit;
        //디버그 코드 2줄
        bool rayResult = Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer);

        Debug.Log("Raycast result: " + rayResult);


        if (!Physics.Raycast(ray, out hit, Mathf.Infinity, groundLayer))
            return;
        Debug.Log("Hit point: " + hit.point);
        /* 바닥의 XZ 좌표를 사용하고, Y는 발사 위치 높이 유지 */
        Vector3 targetPos = new Vector3(
            hit.point.x,
            firePoint.position.y,
            hit.point.z
        );

        /* 발사 방향 계산 */
        Vector3 direction = (targetPos - firePoint.position).normalized;

        /* 투사체 생성 및 방향 설정 */
        Instantiate(
            projectilePrefab,
            firePoint.position,
            Quaternion.LookRotation(direction)
        );
    }
}
