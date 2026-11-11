using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HopOn.UI
{
    /// <summary>The level-complete screen; Next leads to the following level.</summary>
    [RequireComponent(typeof(UIScreen))]
    public sealed class WinView : MonoBehaviour
    {
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text nextButtonLabel;

        private UIScreen _screen;

        public event Action NextPressed;

        private void Awake()
        {
            _screen = GetComponent<UIScreen>();
            nextButton.onClick.AddListener(OnNextClicked);
        }

        private void OnDestroy()
        {
            nextButton.onClick.RemoveListener(OnNextClicked);
        }

        public void Show(int nextLevelNumber, bool instant = false)
        {
            nextButtonLabel.text = $"Level {nextLevelNumber}";
            _screen.Show(instant);
        }

        public void Hide(bool instant = false)
        {
            _screen.Hide(instant);
        }

        private void OnNextClicked()
        {
            NextPressed?.Invoke();
        }
    }
}
