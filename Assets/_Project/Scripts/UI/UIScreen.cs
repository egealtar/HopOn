using DG.Tweening;
using UnityEngine;

namespace HopOn.UI
{
    /// <summary>
    /// One full-screen UI screen (Start, HUD, Win, Lose).
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

        public bool IsVisible { get; private set; }

        // Fetched on first use, not in Awake: another script may call Show/Hide from its own
        // Awake, and Unity does not say which object's Awake runs first.
        private Canvas Canvas => _canvas != null ? _canvas : (_canvas = GetComponent<Canvas>());
        private CanvasGroup Group => _group != null ? _group : (_group = GetComponent<CanvasGroup>());

        public void Show(bool instant = false)
        {
            IsVisible = true;
            Group.DOKill();             // one tween owner: a running fade must not fight this one
            Canvas.enabled = true;
            if (instant)
            {
                Group.alpha = 1f;
                SetInput(true);
                return;
            }
            SetInput(false);
            Group.DOFade(1f, fadeSeconds)
                .SetUpdate(true)        // unscaled time: still animates if the game is paused
                .OnComplete(() => SetInput(true));
        }

        public void Hide(bool instant = false)
        {
            IsVisible = false;
            Group.DOKill();
            SetInput(false);
            if (instant)
            {
                Group.alpha = 0f;
                Canvas.enabled = false;
                return;
            }
            Group.DOFade(0f, fadeSeconds)
                .SetUpdate(true)
                .OnComplete(() => Canvas.enabled = false);
        }

        private void SetInput(bool on)
        {
            Group.interactable = on;
            Group.blocksRaycasts = on;
        }

        private void OnDestroy()
        {
            Group.DOKill();
        }
    }
}
