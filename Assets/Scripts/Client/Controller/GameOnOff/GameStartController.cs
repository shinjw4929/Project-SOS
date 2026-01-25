using UnityEngine;

namespace Client
{
    public class GameStartController : MonoBehaviour
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