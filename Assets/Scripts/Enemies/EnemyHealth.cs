using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public int maxHp = 100;
    public int currentHp;

    void Awake()
    {
        currentHp = maxHp;
    }

    public void TakeDamage(int dmg)
    {
        currentHp -= dmg;

        if (currentHp <= 0)
        {
            Destroy(gameObject);
        }
    }
}
