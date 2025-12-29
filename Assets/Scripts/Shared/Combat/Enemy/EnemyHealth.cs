using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] private int maxHp = 100;

    public int MaxHp => maxHp;
    public int CurrentHp { get; private set; }

    private void Awake()
    {
        CurrentHp = maxHp;
    }

    public void TakeDamage(int amount)
    {
        if (CurrentHp <= 0) return;

        CurrentHp -= amount;
        if (CurrentHp < 0) CurrentHp = 0;

        if (CurrentHp == 0)
        {
            Die();
        }
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
