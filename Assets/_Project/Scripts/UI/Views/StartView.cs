using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HopOn.UI
{
    /// <summary>
    /// The start screen. Knows its own widgets; the flow only says what to show and hears when
    /// Play is pressed. It never calls the flow back, it only announces.
    /// </summary>
    [RequireComponent(typeof(UIScreen))]
    public sealed class StartView : MonoBehaviour
    {
        [SerializeField] private Button playButton;
        [SerializeField] private TMP_Text playButtonLabel;
        [SerializeField] private TMP_Text timeText;

        private UIScreen _screen;

        public event Action PlayPressed;

        private void Awake()
        {
            _screen = GetComponent<UIScreen>();
            playButton.onClick.AddListener(OnPlayClicked);
        }

        private void OnDestroy()
        {
            playButton.onClick.RemoveListener(OnPlayClicked);
        }

        public void Show(int levelNumber, int seconds, bool instant = false)
        {
            playButtonLabel.text = $"Level {levelNumber}";
            timeText.text = TimeFormat.Clock(seconds);
            _screen.Show(instant);
        }

        public void Hide(bool instant = false)
        {
            _screen.Hide(instant);
        }

        private void OnPlayClicked()
        {
            PlayPressed?.Invoke();
        }
    }
}
