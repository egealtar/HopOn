using System.IO;
using HopOn.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HopOn.EditorTools
{
    /// <summary>
    /// The world's colour grade without a post-processing pass (see Art/Grading/Shaders/HopOnGrade.hlsl).
    ///
    /// Hop On > Bake Color Grade reads the post-processing Volume profile, exactly as URP's
    /// ColorGradingLutPass would (every component: Color Adjustments, White Balance, Channel Mixer,
    /// Lift Gamma Gain, Shadows Midtones Highlights, Split Toning, Color Curves), renders the LDR LUT with
    /// URP's own LutBuilderLdr shader, saves it as Art/Grading/HopOn_GradeLut.png and fills
    /// Resources/ColorGrade.asset with it, the post exposure and the Vignette. So the look is still edited
    /// in the Volume as before: change it, bake again.
    ///
    /// Hop On > Switch World To Graded Shaders (once): the world's URP Simple Lit and Lit materials move to
    /// HopOn/Simple Lit Graded and HopOn/Lit Graded (URP's shaders with the grade at the end of the forward
    /// pass), the start street's far-view sprites get HopOn/Sprite Graded, and the old UI vignette overlay
    /// goes off, since the shaders now do the vignette.
    ///
    /// With the Editor closed: Unity -batchmode -quit -projectPath . -executeMethod HopOn.EditorTools.ColorGradeBaker.BakeAndSwitch
    /// </summary>
    [InitializeOnLoad]
    public static class ColorGradeBaker
    {
        private const string ProfilePath = "Assets/_Project/Settings/HopOn_PostProcess.asset";
        private const string GradingFolder = "Assets/_Project/Art/Grading";
        private const string LutPath = GradingFolder + "/HopOn_GradeLut.png";
        private const string SpriteMaterialPath = GradingFolder + "/M_SpriteGraded.mat";
        private const string GradeAssetPath = "Assets/_Project/Resources/" + ColorGrade.ResourceName + ".asset";
        private const string ArtFolder = "Assets/_Project/Art";
        private static readonly string[] Scenes = { "Assets/_Project/Scenes/Boot.unity", "Assets/_Project/Scenes/Game.unity" };

        // The Scene and Game views show the grade too.
        static ColorGradeBaker()
        {
            EditorApplication.delayCall += ApplyInEditor;
        }

        private static void ApplyInEditor()
        {
            var grade = AssetDatabase.LoadAssetAtPath<ColorGrade>(GradeAssetPath);
            if (grade != null)
            {
                grade.Apply();
            }
        }

        public static void BakeAndSwitch()
        {
            Bake();
            SwitchWorldToGradedShaders();
        }

        [MenuItem("Hop On/Bake Color Grade")]
        public static void Bake()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogError($"Color grade: no Volume profile at {ProfilePath}.");
                return;
            }
            var colorAdjustments = Resolve<ColorAdjustments>(profile);
            var channelMixer = Resolve<ChannelMixer>(profile);
            var curves = Resolve<ColorCurves>(profile);
            var liftGammaGain = Resolve<LiftGammaGain>(profile);
            var shadowsMidtonesHighlights = Resolve<ShadowsMidtonesHighlights>(profile);
            var splitToning = Resolve<SplitToning>(profile);
            var tonemapping = Resolve<Tonemapping>(profile);
            var whiteBalance = Resolve<WhiteBalance>(profile);
            var vignette = Resolve<Vignette>(profile);
            if (tonemapping.mode.value != TonemappingMode.None)
            {
                Debug.LogWarning("Color grade: the Volume has tonemapping; the in-shader grade does not do tonemapping.");
            }

            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            int size = urp != null ? urp.colorGradingLutSize : 32;
            Texture2D lut = RenderLut(size, colorAdjustments, channelMixer, curves, liftGammaGain,
                shadowsMidtonesHighlights, splitToning, whiteBalance);
            Directory.CreateDirectory(GradingFolder);
            File.WriteAllBytes(LutPath, lut.EncodeToPNG());
            Object.DestroyImmediate(lut);
            AssetDatabase.ImportAsset(LutPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(LutPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            var grade = AssetDatabase.LoadAssetAtPath<ColorGrade>(GradeAssetPath);
            if (grade == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GradeAssetPath));
                grade = ScriptableObject.CreateInstance<ColorGrade>();
                AssetDatabase.CreateAsset(grade, GradeAssetPath);
            }
            var serialized = new SerializedObject(grade);
            serialized.FindProperty("lut").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(LutPath);
            serialized.FindProperty("postExposure").floatValue = Mathf.Pow(2f, colorAdjustments.postExposure.value);
            bool vignetteOn = vignette.active && vignette.intensity.value > 0f;
            serialized.FindProperty("vignette").vector4Value = vignetteOn
                ? new Vector4(vignette.intensity.value * 3f, vignette.smoothness.value * 5f, vignette.rounded.value ? 1f : 0f, 0f)
                : Vector4.zero;
            serialized.FindProperty("vignetteCenter").vector2Value = vignette.center.value;
            serialized.FindProperty("vignetteColor").colorValue = vignette.color.value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grade);
            AssetDatabase.SaveAssets();
            grade.Apply();
            Debug.Log($"Color grade: baked a {size}x{size}x{size} LUT from {ProfilePath} into {LutPath}.");
        }

        // A Volume component as the volume stack would give it: defaults, overridden where the profile overrides.
        private static T Resolve<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var resolved = ScriptableObject.CreateInstance<T>();
            if (profile.TryGet(out T source) && source.active)
            {
                for (int i = 0; i < source.parameters.Count; i++)
                {
                    if (source.parameters[i].overrideState)
                    {
                        resolved.parameters[i].SetValue(source.parameters[i]);
                    }
                }
            }
            return resolved;
        }

        // The same material setup as URP 14's ColorGradingLutPass for an LDR LUT, then one full-screen triangle.
        private static Texture2D RenderLut(int size, ColorAdjustments colorAdjustments, ChannelMixer channelMixer,
            ColorCurves curves, LiftGammaGain liftGammaGain, ShadowsMidtonesHighlights shadowsMidtonesHighlights,
            SplitToning splitToning, WhiteBalance whiteBalance)
        {
            var material = new Material(Shader.Find("Hidden/Universal Render Pipeline/LutBuilderLdr"));
            int width = size * size;
            var (shadows, midtones, highlights) = ColorUtils.PrepareShadowsMidtonesHighlights(
                shadowsMidtonesHighlights.shadows.value, shadowsMidtonesHighlights.midtones.value,
                shadowsMidtonesHighlights.highlights.value);
            var (lift, gamma, gain) = ColorUtils.PrepareLiftGammaGain(
                liftGammaGain.lift.value, liftGammaGain.gamma.value, liftGammaGain.gain.value);
            var (splitShadows, splitHighlights) = ColorUtils.PrepareSplitToning(
                splitToning.shadows.value, splitToning.highlights.value, splitToning.balance.value);
            material.SetVector("_Lut_Params", new Vector4(size, 0.5f / width, 0.5f / size, size / (size - 1f)));
            material.SetVector("_ColorBalance", ColorUtils.ColorBalanceToLMSCoeffs(whiteBalance.temperature.value, whiteBalance.tint.value));
            material.SetVector("_ColorFilter", colorAdjustments.colorFilter.value.linear);
            material.SetVector("_ChannelMixerRed", new Vector4(channelMixer.redOutRedIn.value / 100f,
                channelMixer.redOutGreenIn.value / 100f, channelMixer.redOutBlueIn.value / 100f, 0f));
            material.SetVector("_ChannelMixerGreen", new Vector4(channelMixer.greenOutRedIn.value / 100f,
                channelMixer.greenOutGreenIn.value / 100f, channelMixer.greenOutBlueIn.value / 100f, 0f));
            material.SetVector("_ChannelMixerBlue", new Vector4(channelMixer.blueOutRedIn.value / 100f,
                channelMixer.blueOutGreenIn.value / 100f, channelMixer.blueOutBlueIn.value / 100f, 0f));
            material.SetVector("_HueSatCon", new Vector4(colorAdjustments.hueShift.value / 360f,
                colorAdjustments.saturation.value / 100f + 1f, colorAdjustments.contrast.value / 100f + 1f, 0f));
            material.SetVector("_Lift", lift);
            material.SetVector("_Gamma", gamma);
            material.SetVector("_Gain", gain);
            material.SetVector("_Shadows", shadows);
            material.SetVector("_Midtones", midtones);
            material.SetVector("_Highlights", highlights);
            material.SetVector("_ShaHiLimits", new Vector4(shadowsMidtonesHighlights.shadowsStart.value,
                shadowsMidtonesHighlights.shadowsEnd.value, shadowsMidtonesHighlights.highlightsStart.value,
                shadowsMidtonesHighlights.highlightsEnd.value));
            material.SetVector("_SplitShadows", splitShadows);
            material.SetVector("_SplitHighlights", splitHighlights);
            material.SetTexture("_CurveMaster", curves.master.value.GetTexture());
            material.SetTexture("_CurveRed", curves.red.value.GetTexture());
            material.SetTexture("_CurveGreen", curves.green.value.GetTexture());
            material.SetTexture("_CurveBlue", curves.blue.value.GetTexture());
            material.SetTexture("_CurveHueVsHue", curves.hueVsHue.value.GetTexture());
            material.SetTexture("_CurveHueVsSat", curves.hueVsSat.value.GetTexture());
            material.SetTexture("_CurveLumVsSat", curves.lumVsSat.value.GetTexture());
            material.SetTexture("_CurveSatVsSat", curves.satVsSat.value.GetTexture());

            var target = new RenderTexture(width, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var commands = new CommandBuffer { name = "Bake colour grade LUT" };
            commands.SetRenderTarget(target);
            commands.SetGlobalVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
            commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var lut = new Texture2D(width, size, TextureFormat.RGBA32, false, true);
            lut.ReadPixels(new Rect(0, 0, width, size), 0, 0);
            lut.Apply();
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(material);
            return lut;
        }

        [MenuItem("Hop On/Switch World To Graded Shaders")]
        public static void SwitchWorldToGradedShaders()
        {
            Shader simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader simpleLitGraded = Shader.Find("HopOn/Simple Lit Graded");
            Shader litGraded = Shader.Find("HopOn/Lit Graded");
            int switched = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ArtFolder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Shader graded = material.shader == simpleLit ? simpleLitGraded : material.shader == lit ? litGraded : null;
                if (graded == null)
                {
                    continue;
                }
                int queue = material.renderQueue;
                material.shader = graded;
                material.renderQueue = queue;
                EditorUtility.SetDirty(material);
                switched++;
            }

            var spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);
            if (spriteMaterial == null)
            {
                spriteMaterial = new Material(Shader.Find("HopOn/Sprite Graded")) { name = "M_SpriteGraded" };
                AssetDatabase.CreateAsset(spriteMaterial, SpriteMaterialPath);
            }
            AssetDatabase.SaveAssets();

            foreach (string path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int sprites = 0;
                foreach (StartTown town in Object.FindObjectsOfType<StartTown>(true))
                {
                    foreach (SpriteRenderer sprite in town.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        sprite.sharedMaterial = spriteMaterial;
                        EditorUtility.SetDirty(sprite);
                        sprites++;
                    }
                }
                int overlays = 0;
                foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>(true))
                {
                    Transform overlay = canvas.isRootCanvas ? canvas.transform.Find("Vignette") : null;
                    if (overlay != null && overlay.gameObject.activeSelf)
                    {
                        overlay.gameObject.SetActive(false);
                        overlays++;
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"Color grade: {path}: {sprites} far-view sprites graded, {overlays} UI vignette overlays off.");
            }
            Debug.Log($"Color grade: {switched} world materials moved to the graded shaders.");
        }
    }
}
