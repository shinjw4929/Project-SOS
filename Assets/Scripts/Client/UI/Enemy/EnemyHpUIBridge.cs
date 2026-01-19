// using TMPro;
// using UnityEngine;
//
// public class EnemyHpUIBridge : MonoBehaviour
// {
//     public static EnemyHpUIBridge Instance { get; private set; }
//
//     [Header("TextMeshPro 3D Prefab")]
//     public TextMeshPro text3dPrefab;
//
//     [Header("머리 위 높이")]
//     public float heightOffset = 2.0f;
//
//     [Header("크기")]
//     public float uniformScale = 1.0f;
//
//     private void OnEnable()
//     {
//         Instance = this;
//     }
//
//     private void OnDisable()
//     {
//         if (Instance == this) Instance = null;
//     }
// }
