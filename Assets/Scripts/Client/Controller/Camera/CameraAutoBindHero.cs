using UnityEngine;

// 씬에 생성된 히어로를 찾아서 Main Camera의 Follow 컴포넌트에 연결해주는 바인더
// - 태그(PlayerHero)로 히어로 GameObject를 찾는다.
// - 한번 찾으면 SetTarget 후 스스로 비활성화해서 불필요한 Find 호출을 막는다.
public class CameraAutoBindHero : MonoBehaviour
{
    // 같은 오브젝트(Main Camera)에 붙어있는 Follow 컴포넌트를 참조
    [SerializeField] private ThirdPersonCameraFollow follow;

    // 히어로에 붙여둘 태그 이름
    [SerializeField] private string heroTag = "PlayerHero";

    private void Awake()
    {
        // 인스펙터에서 연결하지 않았으면 같은 오브젝트에서 자동으로 가져온다.
        if (follow == null) follow = GetComponent<ThirdPersonCameraFollow>();
    }

    private void LateUpdate()
    {
        if (follow == null) return;

        // 씬에 히어로가 아직 생성되지 않았을 수 있으므로, 나타날 때까지 찾는다.
        // 히어로가 생성되면 Follow 타겟으로 지정하고, 이 스크립트는 더 이상 돌 필요가 없다.
        var hero = GameObject.FindWithTag(heroTag);
        if (hero != null)
        {
            follow.SetTarget(hero.transform);
            enabled = false;
        }
    }
}
