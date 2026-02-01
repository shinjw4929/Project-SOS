// [주석처리] HeroHpTextManager - 비활성화됨
/*
using TMPro;
using UnityEngine;

// 다른 스크립트가 HealthText를 덮어써도, 렌더 직전에 다시 써서 최종 표시를 고정한다.
[DefaultExecutionOrder(10000)]
public class HeroHpTextManager : MonoBehaviour
{
    [SerializeField] private TMP_Text healthText;

    private int currentHp;
    private int maxHp = 100;
    private bool hasValue;

    public void SetHp(int cur, int max)
    {
        currentHp = Mathf.Max(0, cur);
        maxHp = Mathf.Max(1, max);
        hasValue = true;
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += ApplyText;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ApplyText;
    }

    private void ApplyText()
    {
        if (!hasValue) return;
        if (healthText == null) return;

        healthText.text = $"HP:\n{currentHp}/{maxHp}";
    }
}
*/
