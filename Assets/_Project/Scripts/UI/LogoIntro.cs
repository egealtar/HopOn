using DG.Tweening;
using UnityEngine;

namespace HopOn.UI
{
    /// <summary>
    /// The start logo's way in after the boot scene. It starts where the boot scene left it (in the
    /// middle, bigger, over the same sunset picture), so the change of scene cannot be seen, then
    /// flies up to its own place while the picture fades and the 3D street shows; the Level button
    /// and the timer come last. Its own place is read from the layout, so it lands right on any
    /// screen. Once per launch: Next and Retry show the start screen without it.
    /// </summary>
    public sealed class LogoIntro : MonoBehaviour
    {
        private static bool _played;

        [SerializeField] private RectTransform logo;
        [Tooltip("Must equal the Scale of the Logo in the Boot scene: the logo starts at that size, so the change of scene cannot be seen.")]
        [SerializeField] private float bootScale = 1f;
        [Tooltip("The sunset picture over the street; the same picture as the boot scene's.")]
        [SerializeField] private CanvasGroup curtain;
        [Tooltip("The Level button and the timer: shown last, and not tappable before.")]
        [SerializeField] private CanvasGroup[] controls;
        [SerializeField] private float holdSeconds = 0.15f;
        [SerializeField] private float flySeconds = 0.75f;
        [SerializeField] private float controlsSeconds = 0.3f;

        private void Start()
        {
            if (_played)
            {
                curtain.gameObject.SetActive(false);
                return;
            }
            _played = true;

            // Where the logo lives on this screen, before it is moved to the boot scene's pose.
            Vector2 anchorMin = logo.anchorMin;
            Vector2 anchorMax = logo.anchorMax;
            Vector2 position = logo.anchoredPosition;
            Vector3 scale = logo.localScale;
            logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 0.5f);
            logo.anchoredPosition = Vector2.zero;
            logo.localScale = scale * bootScale;
            foreach (CanvasGroup group in controls)
            {
                SetShown(group, false);
            }

            Sequence intro = DOTween.Sequence().SetLink(gameObject);
            intro.AppendInterval(holdSeconds);
            intro.Append(logo.DOAnchorMin(anchorMin, flySeconds).SetEase(Ease.InOutCubic));
            intro.Join(logo.DOAnchorMax(anchorMax, flySeconds).SetEase(Ease.InOutCubic));
            intro.Join(logo.DOAnchorPos(position, flySeconds).SetEase(Ease.InOutCubic));
            intro.Join(logo.DOScale(scale, flySeconds).SetEase(Ease.OutBack));
            intro.Join(curtain.DOFade(0f, flySeconds));
            foreach (CanvasGroup group in controls)
            {
                intro.Insert(holdSeconds + flySeconds, group.DOFade(1f, controlsSeconds));
            }
            intro.OnComplete(Finish);
        }

        private void Finish()
        {
            curtain.gameObject.SetActive(false);
            foreach (CanvasGroup group in controls)
            {
                SetShown(group, true);
            }
        }

        private static void SetShown(CanvasGroup group, bool shown)
        {
            if (!shown)
            {
                group.alpha = 0f;
            }
            group.interactable = shown;
            group.blocksRaycasts = shown;
        }
    }
}
