using DG.Tweening;
using UnityEngine;

namespace HopOn.UI
{
    /// <summary>
    /// One full-screen UI screen (Start, HUD, Win, Lose), a nested Canvas under the root Canvas.
    /// Each screen has its own Canvas, so a change on one screen (the HUD timer ticking) never
    /// rebuilds the others, and a hidden screen costs nothing to draw.
    /// Show: Canvas on, fade in, input on at the end (no tapping a half-visible button).
    /// Hide: input off at once (taps pass through right away), fade out, then Canvas off.
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
    public sealed class UIScreen : MonoBehaviour
    {
        [SerializeField] private float fadeSeconds = 0.25f;

        private Canvas _canvas;
        private CanvasGroup _group;

        /// <summary>How long Show and Hide take.</summary>
        public float FadeSeconds => fadeSeconds;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _group = GetComponent<CanvasGroup>();
        }

        public void Show(bool instant = false)
        {
            _group.DOKill();            // one tween owner: a running fade must not fight this one
            _canvas.enabled = true;
            if (instant)
            {
                _group.alpha = 1f;
                SetInput(true);
                return;
            }
            SetInput(false);
            _group.DOFade(1f, fadeSeconds)
                .SetUpdate(true)        // unscaled time: still animates if the game is paused
                .OnComplete(EnableInput);
        }

        public void Hide(bool instant = false)
        {
            _group.DOKill();
            SetInput(false);
            if (instant)
            {
                _group.alpha = 0f;
                _canvas.enabled = false;
                return;
            }
            _group.DOFade(0f, fadeSeconds)
                .SetUpdate(true)
                .OnComplete(DisableCanvas);
        }

        private void EnableInput()
        {
            SetInput(true);
        }

        private void DisableCanvas()
        {
            _canvas.enabled = false;
        }

        private void SetInput(bool on)
        {
            _group.interactable = on;
            _group.blocksRaycasts = on;
        }

        private void OnDestroy()
        {
            _group.DOKill();
        }
    }
}
