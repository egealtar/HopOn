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
        [Tooltip("The 3D scenery behind this screen (the start town). On while the screen shows, off once Play is pressed.")]
        [SerializeField] private GameObject scenery;

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

        /// <param name="seconds">The time the player will have: the full limit, or what is left of a resumed level.</param>
        public void Show(int levelNumber, float seconds, bool instant = false)
        {
            playButton.interactable = true;
            playButtonLabel.text = $"Level {levelNumber}";
            timeText.text = TimeFormat.Clock(seconds);
            SetScenery(true);
            _screen.Show(instant);
        }

        public void Hide(bool instant = false)
        {
            SetScenery(false);
            _screen.Hide(instant);
        }

        private void SetScenery(bool on)
        {
            if (scenery != null)
            {
                scenery.SetActive(on);
            }
        }

        private void OnPlayClicked()
        {
            // Locked at once: a second tap cannot start the level twice, and the button plays its
            // Disabled animation (the pop) while the curtain waits for it.
            playButton.interactable = false;
            PlayPressed?.Invoke();
        }
    }
}
