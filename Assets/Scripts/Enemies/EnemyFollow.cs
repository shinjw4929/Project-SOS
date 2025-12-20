using UnityEngine;

public class EnemyFollow : MonoBehaviour
{
    public Transform player;      // 따라갈 대상
    public float moveSpeed = 3f;   // 이동 속도

    void Update()
    {
        if (player == null) return;

        // 방향 계산
        Vector3 dir = (player.position - transform.position).normalized;

        // 이동 (Y 고정)
        transform.position += new Vector3(dir.x, 0, dir.z) * moveSpeed * Time.deltaTime;
    }
    
    //범위 설정후 추적
    //public float detectRange = 10f;

    //void Update()
    //{
    //    if (player == null) return;

    //    float dist = Vector3.Distance(transform.position, player.position);
    //    if (dist > detectRange) return;

    //    Vector3 dir = (player.position - transform.position).normalized;
    //    transform.position += new Vector3(dir.x, 0, dir.z) * moveSpeed * Time.deltaTime;
    //}

}
