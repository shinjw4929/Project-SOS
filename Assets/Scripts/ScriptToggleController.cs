using UnityEngine;

public class ScriptToggleController : MonoBehaviour
{
    [SerializeField] private Behaviour[] toggleTargets;

    void Update()
    {
        // 1번키는 항상 on/off 기능을함
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            ToggleBuildMode();
        }

        // 오른쪽키는 건설중일때만 off의 기능을 함
        if (IsBuilding() && Input.GetMouseButtonDown(1))
        {
            SetBuildMode(false);
        }
    }

    // true = 건설, false = 안건설
    // 현재 건설 모드인지 여부를 반환한다
    // 하나라도 활성화된 대상이 있으면 건설 중으로 판단
    bool IsBuilding()
    {
        foreach (var target in toggleTargets)
        {
            if (target != null && target.enabled)
                return true;
        }
        return false;
    }
    // 건설 모드 상태를 토글한다 (ON ↔ OFF)
    void ToggleBuildMode()
    {
        bool nextState = !IsBuilding();
        SetBuildMode(nextState);
    }
    // 건설 모드 상태를 설정한다
    // true  : 건설 중 상태
    // false : 일반 상태 (건설 아님)
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
