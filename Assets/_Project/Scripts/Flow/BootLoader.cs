using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HopOn.Flow
{
    /// <summary>
    /// The first scene. It shows the logo (its entrance is an Animation clip on the logo) while the
    /// game scene loads in the background, and opens the game scene once both are done. The game
    /// scene's first frame shows the same picture and the same logo (see LogoIntro), so neither the
    /// change of scene nor the hitch of opening a big scene can be seen.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        [SerializeField] private string gameScene = "Game";
        [Tooltip("On screen at least this long: the length of the logo's entrance clip.")]
        [SerializeField] private float minimumSeconds = 1.9f;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;   // GameFlow sets the same once the game scene runs
            AsyncOperation loading = SceneManager.LoadSceneAsync(gameScene);
            loading.allowSceneActivation = false;
            yield return new WaitForSeconds(minimumSeconds);
            // Loading stops at 0.9 until it is allowed to open the scene.
            while (loading.progress < 0.9f)
            {
                yield return null;
            }
            loading.allowSceneActivation = true;
        }
    }
}
