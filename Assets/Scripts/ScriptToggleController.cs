using UnityEngine;

public class ScriptToggleController : MonoBehaviour
{
    [SerializeField] private Behaviour[] toggleTargets;

    void Update()
    {
        // 1 key : toggle build mode
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ToggleBuildMode();
        }

        // Right click : exit build mode only when building
        if (IsBuilding() && Input.GetMouseButtonDown(1))
        {
            SetBuildMode(false);
        }
    }

    // true = building, false = not building
    bool IsBuilding()
    {
        foreach (var target in toggleTargets)
        {
            if (target != null && target.enabled)
                return true;
        }
        return false;
    }

    void ToggleBuildMode()
    {
        bool nextState = !IsBuilding();
        SetBuildMode(nextState);
    }

    void SetBuildMode(bool value)
    {
        foreach (var target in toggleTargets)
        {
            if (target != null)
            {
                target.enabled = value;
            }
        }
    }
}
