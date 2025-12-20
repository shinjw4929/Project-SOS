using UnityEngine;
using TMPro;

public class PlayerHpUI : MonoBehaviour
{//100/100 텍스트를 최대체력과 현재체력 지정해준거
    public PlayerHealth playerHealth;
    public TextMeshProUGUI hpText;

    void Update()
    {
        hpText.text = $"{playerHealth.hp} / {playerHealth.maxHp}";
    }
}
