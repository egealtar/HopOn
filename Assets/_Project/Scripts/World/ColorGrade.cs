using UnityEngine;
using UnityEngine.SceneManagement;

namespace HopOn.World
{
    /// <summary>
    /// The world's colour grade, applied inside the world's shaders (Art/Grading/Shaders/HopOnGrade.hlsl)
    /// instead of by a post-processing pass: a colour-grading LUT, the post exposure and the vignette, all
    /// matching the post-processing Volume. Before the first scene loads it
    /// hands them to every shader as globals. It lives in Resources (Resources/ColorGrade.asset), so nothing
    /// has to reference it.
    /// </summary>
    public sealed class ColorGrade : ScriptableObject
    {
        public const string ResourceName = "ColorGrade";

        private static readonly int LutId = Shader.PropertyToID("_HopOnGradeLut");
        private static readonly int LutParamsId = Shader.PropertyToID("_HopOnGradeLutParams");
        private static readonly int VignetteId = Shader.PropertyToID("_HopOnVignette");
        private static readonly int VignetteCenterId = Shader.PropertyToID("_HopOnVignetteCenter");
        private static readonly int VignetteColorId = Shader.PropertyToID("_HopOnVignetteColor");

        [Tooltip("The LDR colour-grading LUT strip (size x size wide, size high), linear, as URP builds it.")]
        [SerializeField] private Texture2D lut;
        [Tooltip("The Volume's post exposure, as a multiplier (2 to the power of the EV).")]
        [SerializeField] private float postExposure = 1f;
        [Tooltip("x: intensity x 3, y: smoothness x 5, z: 1 if rounded (a circle on any screen), w: unused.")]
        [SerializeField] private Vector4 vignette;
        [SerializeField] private Vector2 vignetteCenter = new Vector2(0.5f, 0.5f);
        [SerializeField] private Color vignetteColor = Color.black;

        // Held here and marked so that a scene load's unloading of unused assets keeps it: a global shader
        // property is not a reference, so without this the LUT was unloaded on the way into the game scene
        // and every graded shader read Unity's grey default texture.
        private static ColorGrade _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStart()
        {
            _active = Resources.Load<ColorGrade>(ResourceName);
            if (_active == null)
            {
                return;
            }
            _active.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (_active.lut != null)
            {
                _active.lut.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            _active.Apply();
            SceneManager.sceneLoaded += ApplyAgain;
        }

        private static void ApplyAgain(Scene scene, LoadSceneMode mode)
        {
            _active.Apply();
        }

        /// <summary>Hands the grade to every shader. Without a LUT, colours pass through ungraded.</summary>
        public void Apply()
        {
            if (lut == null)
            {
                Shader.SetGlobalVector(LutParamsId, Vector4.zero);
                return;
            }
            Shader.SetGlobalTexture(LutId, lut);
            Shader.SetGlobalVector(LutParamsId, new Vector4(1f / lut.width, 1f / lut.height, lut.height - 1f, postExposure));
            Shader.SetGlobalVector(VignetteId, vignette);
            Shader.SetGlobalVector(VignetteCenterId, vignetteCenter);
            Shader.SetGlobalVector(VignetteColorId, vignetteColor);
        }
    }
}
