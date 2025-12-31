using UnityEngine;

public class ProjectileDamage : MonoBehaviour
{
    [SerializeField] private int damage = 1;

    private void OnTriggerEnter(Collider other)
    {
        EnemyHealth enemy = other.GetComponentInParent<EnemyHealth>();
        if (!enemy) return; // Unity Object는 implicit bool 사용

        enemy.TakeDamage(damage);
        Destroy(gameObject);
    }
}
