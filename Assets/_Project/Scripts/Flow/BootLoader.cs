using System.Collections;
using DG.Tweening;
using HopOn.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HopOn.Flow
{
    /// <summary>
    /// The first scene. It shows the logo on a flat colour (it appears where it stands: an Animation clip on
    /// the logo) while the game scene loads in the background; once it has loaded, the street builds
    /// itself behind the logo: the colour melts off the ground half as the road comes and off the sky
    /// half as the houses come, and then the game scene opens. Building only after loading keeps loading's hitches out
    /// of the street's animation. The game scene's first frame shows the same picture and the same
    /// logo (see LogoIntro), so neither the change of scene nor the hitch of opening it can be seen.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        [SerializeField] private string gameScene = "Game";
        [Tooltip("The street behind the logo: hidden until the game scene has loaded, then it builds.")]
        [SerializeField] private StartTown town;
        [Tooltip("The first screen's flat colour over the ground half, up to the horizon: melts away as the road comes.")]
        [SerializeField] private Image groundBackdrop;
        [Tooltip("The same colour over the sky half, its lower edge soft: melts away as the houses come.")]
        [SerializeField] private Image skyBackdrop;
        [Tooltip("How long the ground half takes to melt away.")]
        [SerializeField] private float backdropSeconds = 0.38f;
        [Tooltip("When the sky half starts to melt, after the street starts to build: with the houses.")]
        [SerializeField] private float skyBackdropDelay = 0.04f;
        [Tooltip("How long the sky half takes to melt away.")]
        [SerializeField] private float skyBackdropSeconds = 0.45f;
        [Tooltip("The street never starts before this: time for the logo to appear and the screen to settle.")]
        [SerializeField] private float logoSeconds = 0.6f;
        [Tooltip("A still moment with the whole street before the game scene opens.")]
        [SerializeField] private float holdSeconds = 0f;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;   // GameFlow sets the same once the game scene runs
            AsyncOperation loading = SceneManager.LoadSceneAsync(gameScene);
            loading.allowSceneActivation = false;
            yield return new WaitForSeconds(logoSeconds);
            // Loading stops at 0.9 until it is allowed to open the scene.
            while (loading.progress < 0.9f)
            {
                yield return null;
            }
            town.Build();
            groundBackdrop.DOFade(0f, backdropSeconds).SetLink(groundBackdrop.gameObject);
            skyBackdrop.DOFade(0f, skyBackdropSeconds).SetDelay(skyBackdropDelay).SetLink(skyBackdrop.gameObject);
            yield return new WaitForSeconds(town.BuildLength + holdSeconds);
            // The picture stands still now, so opening the scene may take a few long frames instead of
            // many short ones: the start screen comes sooner.
            Application.backgroundLoadingPriority = ThreadPriority.High;
            loading.allowSceneActivation = true;
        }
    }
}
