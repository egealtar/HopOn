using System;
using HopOn.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HopOn.UI
{
    /// <summary>The fail screen; its title and icon name the reason, Retry replays the same level.</summary>
    [RequireComponent(typeof(UIScreen))]
    public sealed class LoseView : MonoBehaviour
    {
        [SerializeField] private Button retryButton;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Image icon;
        [Tooltip("Shown when the time ran out.")]
        [SerializeField] private Sprite timeUpIcon;
        [Tooltip("Shown when the waiting seats are full.")]
        [SerializeField] private Sprite noSpaceIcon;

        private UIScreen _screen;

        public event Action RetryPressed;

        private void Awake()
        {
            _screen = GetComponent<UIScreen>();
            retryButton.onClick.AddListener(OnRetryClicked);
        }

        private void OnDestroy()
        {
            retryButton.onClick.RemoveListener(OnRetryClicked);
        }

        public void Show(LoseReason reason, bool instant = false)
        {
            bool timeUp = reason == LoseReason.TimeUp;
            titleText.text = timeUp ? "Time's Up!" : "No Space Left!";
            icon.sprite = timeUp ? timeUpIcon : noSpaceIcon;
            _screen.Show(instant);
        }

        public void Hide(bool instant = false)
        {
            _screen.Hide(instant);
        }

        private void OnRetryClicked()
        {
            RetryPressed?.Invoke();
        }
    }
}
