using UnityEngine;
using System.Collections;

public class EnemyDamage : MonoBehaviour
{//0.5초마다 1의 데미지
    public int damage = 1;
    public float damageInterval = 0.5f;

    private Coroutine damageCoroutine;
    private bool isDamaging = false;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (isDamaging) return; // 멀어져도 지속뎀 들어오는거 방지

        PlayerHealth ph = other.GetComponent<PlayerHealth>();
        if (ph != null)
        {
            isDamaging = true;
            damageCoroutine = StartCoroutine(DamageOverTime(ph));
        }
    }

    void OnTriggerExit(Collider other)
    {  //이거 이름이 player 아니라 tag가 player 인걸 지정한거 ㅇㅇ
        if (!other.CompareTag("Player")) return;

        StopDamage();
    }

    void OnDisable()
    {
        StopDamage(); // Enemy 비활성화 대비
    }

    void StopDamage()
    {
        if (damageCoroutine != null)
        {
            StopCoroutine(damageCoroutine);
            damageCoroutine = null;
        }
        isDamaging = false;
    }

    IEnumerator DamageOverTime(PlayerHealth ph)
    {
        while (isDamaging)
        {
            ph.TakeDamage(damage);
            yield return new WaitForSeconds(damageInterval);
        }
    }
}
