using TMPro;
using UnityEngine;

public class EnemyHealthUI : MonoBehaviour
{
    [SerializeField] private EnemyHealth enemyHealth;
    [SerializeField] private TextMeshProUGUI hpText;

    private void LateUpdate()
    {
        if (enemyHealth == null || hpText == null) return;
        hpText.text = $"{enemyHealth.CurrentHp}/{enemyHealth.MaxHp}";
    }
}
