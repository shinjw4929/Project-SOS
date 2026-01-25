using UnityEngine;

namespace Client
{
    /// <summary>
    /// 게임 시작 패널 컨트롤러
    /// </summary>
    public class GameStartPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject menuRoot;

        private void Start()
        {
            Time.timeScale = 0f;
            if (menuRoot != null) menuRoot.SetActive(true);
        }

        public void OnClickStart()
        {
            Time.timeScale = 1f;
            if (menuRoot != null) menuRoot.SetActive(false);
        }

        public void OnClickExit()
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
