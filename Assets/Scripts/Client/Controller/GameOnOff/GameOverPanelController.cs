using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client
{
    /// <summary>
    /// 게임오버 패널 컨트롤러
    /// GameOverEvents 이벤트 구독하여 게임오버 패널 표시
    /// </summary>
    public class GameOverPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private TextMeshProUGUI gameOverText;
        [SerializeField] private Button exitButton;

        private void Start()
        {
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(false);
            }

            if (exitButton != null)
            {
                exitButton.onClick.AddListener(OnExitButtonClicked);
            }

            // 이벤트 구독
            GameOverEvents.OnGameOver += ShowGameOverPanel;
        }

        private void ShowGameOverPanel()
        {
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(true);
            }

            if (gameOverText != null)
            {
                gameOverText.text = "GAME OVER";
            }
        }

        private void OnExitButtonClicked()
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void OnDestroy()
        {
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(OnExitButtonClicked);
            }

            // 이벤트 구독 해제
            GameOverEvents.OnGameOver -= ShowGameOverPanel;
        }
    }
}
