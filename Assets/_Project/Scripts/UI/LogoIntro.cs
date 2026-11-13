using DG.Tweening;
using HopOn.World;
using UnityEngine;

namespace HopOn.UI
{
    /// <summary>
    /// The way from the boot scene into the start screen. Both scenes show the same still 3D street
    /// (the StartTown prefab with the same seed) and the logo where the boot scene left it, so the
    /// change of scene cannot be seen. The logo's own Animator plays its letters boarding their bus;
    /// as that bus drives off, the street starts to move, the real bus drives in (its own Animator)
    /// and the wind starts; the Level button and the timer come last.
    /// Once per launch: Next and Retry find the street already moving.
    /// </summary>
    public sealed class LogoIntro : MonoBehaviour
    {
        private static bool _played;

        [Tooltip("The logo's wrapper with the boarding Animator: centred and sized like the boot scene's logo.")]
        [SerializeField] private RectTransform logo;
        [Tooltip("The street behind the start screen: still until the logo has left.")]
        [SerializeField] private StartTown town;
        [Tooltip("Off until the street starts: the bus (it drives in), the wind, the leaves.")]
        [SerializeField] private GameObject[] showOnGo;
        [Tooltip("The Level button and the timer: shown last, and not tappable before.")]
        [SerializeField] private CanvasGroup[] controls;
        [Tooltip("When the street starts: as the logo's bus drives off.")]
        [SerializeField] private float goSeconds = 1.75f;
        [Tooltip("After the street starts: time for the bus to drive in before the button shows.")]
        [SerializeField] private float controlsDelay = 0.5f;
        [SerializeField] private float controlsSeconds = 0.3f;

        private void Start()
        {
            if (_played)
            {
                logo.gameObject.SetActive(false);
                Go();
                return;
            }
            _played = true;
            foreach (CanvasGroup group in controls)
            {
                SetShown(group, false);
            }

            Sequence intro = DOTween.Sequence().SetLink(gameObject);
            intro.AppendInterval(goSeconds);
            intro.AppendCallback(Go);
            foreach (CanvasGroup group in controls)
            {
                intro.Insert(goSeconds + controlsDelay, group.DOFade(1f, controlsSeconds));
            }
            intro.OnComplete(Finish);
        }

        private void Go()
        {
            town.StartMoving();
            foreach (GameObject thing in showOnGo)
            {
                thing.SetActive(true);
            }
        }

        private void Finish()
        {
            logo.gameObject.SetActive(false);
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
