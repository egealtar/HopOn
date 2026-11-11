using System;
using HopOn.Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HopOn.UI
{
    /// <summary>The fail screen; its title names the reason, Retry replays the same level.</summary>
    [RequireComponent(typeof(UIScreen))]
    public sealed class LoseView : MonoBehaviour
    {
        [SerializeField] private Button retryButton;
        [SerializeField] private TMP_Text titleText;

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
            titleText.text = reason == LoseReason.TimeUp ? "Time's Up!" : "No Seats Left!";
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
