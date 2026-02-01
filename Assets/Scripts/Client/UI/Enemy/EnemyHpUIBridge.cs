using TMPro;
using UnityEngine;

public class EnemyHpUIBridge : MonoBehaviour
{
    public static EnemyHpUIBridge Instance { get; private set; }

    [Header("Prefab")]
    public TextMeshPro text3dPrefab;

    [Header("Ground Enemy")]
    public float heightOffset = 2.0f;
    public float uniformScale = 1.0f;

    [Header("Flying Enemy")]
    public float flyingHeightOffset = 1.0f;
    public float flyingScale = 0.5f;

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }
}
