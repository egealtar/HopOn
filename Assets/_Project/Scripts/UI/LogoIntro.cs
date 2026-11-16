using System.Collections;
using DG.Tweening;
using HopOn.World;
using UnityEngine;
using UnityEngine.Serialization;

namespace HopOn.UI
{
    /// <summary>
    /// The start screen's way in, on the first launch. The game scene is the first scene, so Unity loads it
    /// behind its own splash screen. The screen opens on the key art (a UI image over everything, the same
    /// street painted) with the logo on it; the street builds itself behind it while the key art's own Animator
    /// wipes it away bottom to top behind a light bar (the clip is timed against the build). Then the logo's
    /// Animator makes it vanish where it stands. Each clip switches its own object off on its last frame, so no
    /// length is repeated here. As the logo goes, the street starts to move, the real bus drives in
    /// from behind the camera (its own Animator) and the wind starts; the Level button and the timer come last.
    /// Once per launch: Next and Retry find the street built and already moving.
    /// </summary>
    public sealed class LogoIntro : MonoBehaviour
    {
        private static bool _played;

        [Tooltip("The logo's wrapper with the vanish Animator: centred on the screen.")]
        [SerializeField] private RectTransform logo;
        [Tooltip("The street behind the start screen: hidden until it builds, still until the logo goes.")]
        [SerializeField] private StartTown town;
        [Tooltip("Off until the street starts: the bus (it drives in), the wind, the leaves.")]
        [SerializeField] private GameObject[] showOnGo;
        [Tooltip("The Level button and the timer: shown last, and not tappable before.")]
        [SerializeField] private CanvasGroup[] controls;

        [Header("First launch: the key art, wiped away as the street builds behind it")]
        [Tooltip("The key art's Animator, off in the scene: switched on as the street starts to build, its clip " +
                 "wipes the key art away behind the light bar.")]
        [FormerlySerializedAs("backdropWipe")]
        [SerializeField] private Animator keyArt;
        [Tooltip("How long the key art stands, after the first long frames, before the street builds.")]
        [SerializeField] private float buildAfterSeconds = 0.7f;
        [Tooltip("A still moment with the whole street before the logo vanishes.")]
        [SerializeField] private float holdSeconds;

        [Header("Then the logo goes and the street drives")]
        [Tooltip("When the street starts and the real bus drives in from below: while the logo shrinks " +
                 "away, so the bus shows just as it is gone.")]
        [SerializeField] private float goSeconds = 0.2f;
        [Tooltip("After the street starts: time for the bus to drive in before the button shows.")]
        [SerializeField] private float controlsDelay = 0.45f;
        [SerializeField] private float controlsSeconds = 0.25f;

        private IEnumerator Start()
        {
            var vanish = logo.GetComponent<Animator>();
            if (_played)
            {
                keyArt.gameObject.SetActive(false);
                logo.gameObject.SetActive(false);
                Go();
                yield break;
            }
            _played = true;
            foreach (CanvasGroup group in controls)
            {
                SetShown(group, false);
            }
            vanish.enabled = false;
            // The first frames after the splash screen are long (about 0.6 s on a low-end phone: the first
            // render, the UI's first batches): the key art and the logo stand through them.
            yield return null;
            yield return null;
            yield return new WaitForSeconds(buildAfterSeconds);

            town.Build();
            keyArt.enabled = true;              // its clip wipes the key art away and switches it off at its end
            yield return new WaitForSeconds(town.BuildLength + holdSeconds);

            vanish.enabled = true;              // its clip shrinks the logo away and switches it off at its end
            Sequence intro = DOTween.Sequence().SetLink(gameObject);
            intro.InsertCallback(goSeconds, Go);
            foreach (CanvasGroup group in controls)
            {
                intro.Insert(goSeconds + controlsDelay, group.DOFade(1f, controlsSeconds));
            }
            intro.OnComplete(ShowControls);
        }

        private void Go()
        {
            town.StartMoving();
            foreach (GameObject thing in showOnGo)
            {
                thing.SetActive(true);
            }
        }

        private void ShowControls()
        {
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
