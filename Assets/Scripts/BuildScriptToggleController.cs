using UnityEngine;

public class ScriptToggleController : MonoBehaviour
{
    [Header("Toggle Targets")]
    [SerializeField] private Behaviour[] toggleTargets;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Toggle();
        }
    }

    void Toggle()
    {
        foreach (var target in toggleTargets)
        {
            if (target != null)
            {
                target.enabled = !target.enabled;
            }
        }
    }
}
