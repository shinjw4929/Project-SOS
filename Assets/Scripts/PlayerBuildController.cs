using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBuildController : MonoBehaviour
{
    [Header("Input Settings")]
    [SerializeField] private InputActionReference spawnAction; // 인풋 액션 연결

    [Header("Modules")]
    [SerializeField] private ObjectSpawner objectSpawner; // 위에서 만든 스포너 연결

    private void OnEnable()
    {
        // 액션이 설정되어 있다면 이벤트 연결
        if (spawnAction != null)
        {
            spawnAction.action.Enable();
            spawnAction.action.performed += OnSpawnPerformed;
        }
    }

    private void OnDisable()
    {
        if (spawnAction != null)
        {
            spawnAction.action.performed -= OnSpawnPerformed;
            spawnAction.action.Disable();
        }
    }

    // 실제 Q키가 눌렸을 때 실행되는 콜백
    private void OnSpawnPerformed(InputAction.CallbackContext context)
    {
        // 스포너에게 "일해라"라고 명령만 내림
        if (objectSpawner != null)
        {
            objectSpawner.Spawn();
        }
    }
}