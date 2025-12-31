using TMPro;
using UnityEngine;

public class EnemyHealthUI : MonoBehaviour
{
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private TextMeshProUGUI hpText;

    private void LateUpdate()
    {
        if (!enemyHealth || !hpText) return; // Unity Object는 implicit bool 사용
        hpText.text = $"{enemyHealth.CurrentHp}/{enemyHealth.MaxHp}";
    }
}
