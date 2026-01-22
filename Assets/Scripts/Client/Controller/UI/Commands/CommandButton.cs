using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client
{
    /// <summary>
    /// 커맨드 버튼 컴포넌트
    /// - 건물 건설, 유닛 생산, 명령 버튼 등에 재사용
    /// </summary>
    public class CommandButton : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button button;
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text shortcutText;
        [SerializeField] private TMP_Text costText;
        [SerializeField] private Image disabledOverlay;

        [Header("Colors")]
        [SerializeField] private Color normalColor = Color.black;
        [SerializeField] private Color disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);

        private Action _onClick;
        private bool _isUnlocked = true;

        /// <summary>
        /// 버튼 초기화
        /// </summary>
        /// <param name="displayName">표시할 이름</param>
        /// <param name="shortcut">단축키 문자 (예: "Q")</param>
        /// <param name="cost">비용 (-1이면 표시 안 함)</param>
        /// <param name="isUnlocked">해금 여부 (미해금 시 어두운 색상)</param>
        /// <param name="onClick">클릭 콜백</param>
        /// <param name="icon">아이콘 스프라이트 (null이면 표시 안 함)</param>
        public void Setup(string displayName, string shortcut, int cost, bool isUnlocked, Action onClick, Sprite icon = null)
        {
            _onClick = onClick;
            _isUnlocked = isUnlocked;

            // 텍스트 설정 (카멜케이스 분리)
            if (nameText != null)
                nameText.text = SplitCamelCase(displayName);

            if (shortcutText != null)
                shortcutText.text = shortcut;

            if (costText != null)
            {
                if (cost >= 0)
                {
                    costText.text = cost.ToString();
                    costText.gameObject.SetActive(true);
                }
                else
                {
                    costText.gameObject.SetActive(false);
                }
            }

            // 아이콘 설정
            if (iconImage != null)
            {
                if (icon != null)
                {
                    iconImage.sprite = icon;
                    iconImage.gameObject.SetActive(true);
                }
                else
                {
                    iconImage.gameObject.SetActive(false);
                }
            }

            // 해금 상태 적용
            SetUnlocked(isUnlocked);

            // 버튼 리스너 설정
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnButtonClicked);
            }
        }

        /// <summary>
        /// 해금 상태 변경
        /// </summary>
        public void SetUnlocked(bool unlocked)
        {
            _isUnlocked = unlocked;

            if (button != null)
                button.interactable = unlocked;

            if (disabledOverlay != null)
            {
                disabledOverlay.gameObject.SetActive(!unlocked);
                disabledOverlay.color = disabledColor;
            }

            // 이름 텍스트 색상 변경
            if (nameText != null)
                nameText.color = unlocked ? normalColor : disabledColor;
        }

        private void OnButtonClicked()
        {
            if (_isUnlocked && _onClick != null)
            {
                _onClick.Invoke();
            }
        }

        /// <summary>
        /// 버튼 활성화/비활성화
        /// </summary>
        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }

        /// <summary>
        /// 카멜케이스 문자열에 Zero-Width Space 삽입 (자동 줄바꿈 가능하게)
        /// </summary>
        private static string SplitCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new System.Text.StringBuilder(input.Length * 2);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                // 대문자이고 첫 글자가 아니면 앞에 Zero-Width Space 삽입
                if (i > 0 && char.IsUpper(c))
                    sb.Append('\u200B');  // Zero-Width Space
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
