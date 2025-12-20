using UnityEngine;

public class PlayerHealth : MonoBehaviour
{       //하하 주석 쓰기엔 좀 그렇죠?
    public int maxHp = 100;
    public int hp = 100;

    public void TakeDamage(int amount)
    {
        hp -= amount;
        hp = Mathf.Clamp(hp, 0, maxHp);
    }
}
