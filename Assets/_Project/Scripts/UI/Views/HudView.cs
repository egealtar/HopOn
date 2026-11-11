using TMPro;
using UnityEngine;

namespace HopOn.UI
{
    /// <summary>The in-game HUD: level label and countdown.</summary>
    [RequireComponent(typeof(UIScreen))]
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text timeText;

        private UIScreen _screen;
        private int _shownSeconds = -1;

        private void Awake()
        {
            _screen = GetComponent<UIScreen>();
        }

        public void SetLevel(int levelNumber)
        {
            levelText.text = $"Level {levelNumber}";
        }

        // Called every frame by the flow; writes only when the shown second changes, because a
        // text change rebuilds the HUD's canvas.
        public void SetTime(int seconds)
        {
            if (seconds == _shownSeconds)
            {
                return;
            }
            _shownSeconds = seconds;
            timeText.text = TimeFormat.Clock(seconds);
        }

        public void Show(bool instant = false)
        {
            _screen.Show(instant);
        }

        public void Hide(bool instant = false)
        {
            _screen.Hide(instant);
        }
    }
}
