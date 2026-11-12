#if UNITY_EDITOR
using HopOn.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HopOn.Tools
{
    /// <summary>
    /// Editor only (never in a build): one menu item that builds the prototype art from Unity's
    /// basic shapes, so the game has Bus Jam style stick figures and buses without any model files.
    /// It writes the stick figure and bus prefabs, three animation clips (Idle, Walk, Sit) and the
    /// Animator Controller that switches between them. Running it again rebuilds them in place;
    /// the clips can also be opened and edited by hand in the Animation window.
    /// </summary>
    public static class PrototypeArtBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Prototype";
        private const string PrefabFolder = "Assets/_Project/Prefabs";

        // Rest pose: arms hang slightly away from the body.
        private static readonly Vector3 LeftArmRest = new Vector3(0f, 0f, -10f);
        private static readonly Vector3 RightArmRest = new Vector3(0f, 0f, 10f);

        [MenuItem("Hop On/Build Stickman and Bus")]
        public static void Build()
        {
            EnsureFolder(ArtFolder);
            EnsureFolder(PrefabFolder);
            Material paint = SaveMaterial("Prototype_Paint", Color.white);     // recoloured in play
            Material dark = SaveMaterial("Prototype_Dark", new Color(0.17f, 0.18f, 0.24f));

            AnimatorController controller = BuildAnimator();
            Scene scratch = EditorSceneManager.NewPreviewScene();            // builds off-screen, the open scene stays untouched
            try
            {
                BuildStickman(scratch, paint, controller);
                BuildBus(scratch, paint, dark);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scratch);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Hop On: stickman and bus built in {PrefabFolder}, clips and Animator in {ArtFolder}.");
        }

        // ---- The stick figure ----
        // Passenger (PassengerView; DOTween moves this) / Visual (Animator) / Rig / parts.
        // Arms and legs hang from joint objects at the shoulders and hips; the clips turn the joints.

        private static void BuildStickman(Scene scene, Material paint, AnimatorController controller)
        {
            var root = new GameObject("Passenger");
            SceneManager.MoveGameObjectToScene(root, scene);
            var visual = Child(root.transform, "Visual", Vector3.zero);
            var rig = Child(visual, "Rig", Vector3.zero);

            var parts = new[]
            {
                Shape(PrimitiveType.Capsule, "Body", rig, new Vector3(0f, 0.53f, 0f), new Vector3(0.26f, 0.17f, 0.22f), paint),
                Shape(PrimitiveType.Sphere, "Head", rig, new Vector3(0f, 0.84f, 0f), new Vector3(0.3f, 0.3f, 0.3f), paint),
                Limb("ArmL", rig, new Vector3(-0.17f, 0.66f, 0f), LeftArmRest, 0.11f, 0.30f, paint),
                Limb("ArmR", rig, new Vector3(0.17f, 0.66f, 0f), RightArmRest, 0.11f, 0.30f, paint),
                Limb("LegL", rig, new Vector3(-0.08f, 0.38f, 0f), Vector3.zero, 0.13f, 0.38f, paint),
                Limb("LegR", rig, new Vector3(0.08f, 0.38f, 0f), Vector3.zero, 0.13f, 0.38f, paint),
            };

            var animator = visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var view = root.AddComponent<PassengerView>();
            var fields = new SerializedObject(view);
            fields.FindProperty("animator").objectReferenceValue = animator;
            SetArray(fields.FindProperty("bodyParts"), parts);
            fields.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Passenger.prefab");
        }

        // A joint at the shoulder or hip with the limb hanging down from it.
        private static Renderer Limb(string name, Transform rig, Vector3 joint, Vector3 restAngles, float thickness, float length, Material paint)
        {
            Transform pivot = Child(rig, name, joint);
            pivot.localEulerAngles = restAngles;
            return Shape(PrimitiveType.Capsule, "Mesh", pivot, new Vector3(0f, -length / 2f, 0f),
                         new Vector3(thickness, length / 2f, thickness), paint);
        }

        // ---- The bus ----
        // An open-top bus, so the riders can be seen sitting in it from the camera above.

        private static void BuildBus(Scene scene, Material paint, Material dark)
        {
            var root = new GameObject("Bus");
            SceneManager.MoveGameObjectToScene(root, scene);
            var visual = Child(root.transform, "Visual", Vector3.zero);

            var painted = new[]
            {
                Shape(PrimitiveType.Cube, "Body", visual, new Vector3(0f, 0.4f, 0f), new Vector3(2.3f, 0.6f, 1.05f), paint),
                Shape(PrimitiveType.Cube, "Bonnet", visual, new Vector3(1.25f, 0.3f, 0f), new Vector3(0.3f, 0.4f, 0.95f), paint),
            };
            Shape(PrimitiveType.Cube, "Stripe", visual, new Vector3(0f, 0.55f, 0f), new Vector3(2.32f, 0.1f, 1.07f), dark);
            foreach (float x in new[] { -0.75f, 0.75f })
            {
                foreach (float z in new[] { -0.5f, 0.5f })
                {
                    Transform wheel = Shape(PrimitiveType.Cylinder, "Wheel", visual, new Vector3(x, 0.17f, z),
                                            new Vector3(0.34f, 0.05f, 0.34f), dark).transform;
                    wheel.localEulerAngles = new Vector3(90f, 0f, 0f);
                }
            }

            // Three seats on top, riders facing the way the bus drives (+x).
            var seats = new Transform[3];
            for (int i = 0; i < seats.Length; i++)
            {
                seats[i] = Child(root.transform, $"Seat{i}", new Vector3(-0.6f + 0.6f * i, 0.7f, 0f));
                seats[i].localEulerAngles = new Vector3(0f, 90f, 0f);
            }

            var view = root.AddComponent<BusView>();
            var fields = new SerializedObject(view);
            SetArray(fields.FindProperty("paintedParts"), painted);
            SetArray(fields.FindProperty("seats"), seats);
            fields.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Bus.prefab");
        }

        // ---- Animation: three clips and the controller ----
        // Clip paths are relative to Visual, where the Animator sits.

        private static AnimatorController BuildAnimator()
        {
            AnimationClip idle = SaveClip("Passenger_Idle", AddIdleCurves);
            AnimationClip walk = SaveClip("Passenger_Walk", AddWalkCurves);
            AnimationClip sit = SaveClip("Passenger_Sit", AddSitCurves);

            // Three states and two switches. The code sets the switches; never both at once.
            string path = $"{ArtFolder}/Passenger.controller";
            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
            AnimatorStateMachine states = controller.layers[0].stateMachine;
            AnimatorState idleState = states.AddState("Idle");
            idleState.motion = idle;
            AnimatorState walkState = states.AddState("Walk");
            walkState.motion = walk;
            AnimatorState sitState = states.AddState("Sit");
            sitState.motion = sit;
            states.defaultState = idleState;

            AddTransition(states, walkState).AddCondition(AnimatorConditionMode.If, 0f, "Walking");
            AddTransition(states, sitState).AddCondition(AnimatorConditionMode.If, 0f, "Sitting");
            AnimatorStateTransition toIdle = AddTransition(states, idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Walking");
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sitting");
            return controller;
        }

        // Breathing: the body bobs a little and the arms drift out and back.
        private static void AddIdleCurves(AnimationClip clip)
        {
            float[] time = { 0f, 0.8f, 1.6f };
            Position(clip, "Rig", time, new[] { 0f, -0.012f, 0f });
            Rotation(clip, "Rig/ArmL", time, LeftArmRest, new Vector3(0f, 0f, -14f), LeftArmRest);
            Rotation(clip, "Rig/ArmR", time, RightArmRest, new Vector3(0f, 0f, 14f), RightArmRest);
            Rotation(clip, "Rig/LegL", new[] { 0f, 1.6f }, Vector3.zero, Vector3.zero);
            Rotation(clip, "Rig/LegR", new[] { 0f, 1.6f }, Vector3.zero, Vector3.zero);
        }

        // Legs swing opposite to each other, each arm opposite to the leg on its side.
        private static void AddWalkCurves(AnimationClip clip)
        {
            float[] time = { 0f, 0.15f, 0.3f, 0.45f, 0.6f };
            Position(clip, "Rig", time, new[] { 0f, 0.03f, 0f, 0.03f, 0f });
            Rotation(clip, "Rig/LegL", time, X(30f), X(0f), X(-30f), X(0f), X(30f));
            Rotation(clip, "Rig/LegR", time, X(-30f), X(0f), X(30f), X(0f), X(-30f));
            Rotation(clip, "Rig/ArmL", time, Arm(-25f, -10f), Arm(0f, -10f), Arm(25f, -10f), Arm(0f, -10f), Arm(-25f, -10f));
            Rotation(clip, "Rig/ArmR", time, Arm(25f, 10f), Arm(0f, 10f), Arm(-25f, 10f), Arm(0f, 10f), Arm(25f, 10f));
        }

        // Lowered onto the seat, legs forward, hands on the knees.
        private static void AddSitCurves(AnimationClip clip)
        {
            float[] time = { 0f, 1f };
            Position(clip, "Rig", time, new[] { -0.3f, -0.3f });
            Rotation(clip, "Rig/LegL", time, X(-85f), X(-85f));
            Rotation(clip, "Rig/LegR", time, X(-85f), X(-85f));
            Rotation(clip, "Rig/ArmL", time, Arm(-50f, -10f), Arm(-50f, -10f));
            Rotation(clip, "Rig/ArmR", time, Arm(-50f, 10f), Arm(-50f, 10f));
        }

        // From any state, a quick blend into the target; never into itself.
        private static AnimatorStateTransition AddTransition(AnimatorStateMachine states, AnimatorState target)
        {
            AnimatorStateTransition transition = states.AddAnyStateTransition(target);
            transition.hasExitTime = false;
            transition.duration = 0.12f;
            transition.canTransitionToSelf = false;
            return transition;
        }

        // A looping clip; addCurves is the method that writes its keyframes.
        private static AnimationClip SaveClip(string name, System.Action<AnimationClip> addCurves)
        {
            string path = $"{ArtFolder}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            addCurves(clip);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static Vector3 X(float angle) => new Vector3(angle, 0f, 0f);
        private static Vector3 Arm(float swing, float spread) => new Vector3(swing, 0f, spread);

        private static void Rotation(AnimationClip clip, string path, float[] times, params Vector3[] angles)
        {
            SetCurve(clip, path, "localEulerAnglesRaw.x", times, angles, 0);
            SetCurve(clip, path, "localEulerAnglesRaw.y", times, angles, 1);
            SetCurve(clip, path, "localEulerAnglesRaw.z", times, angles, 2);
        }

        private static void Position(AnimationClip clip, string path, float[] times, float[] heights)
        {
            var positions = new Vector3[heights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                positions[i] = new Vector3(0f, heights[i], 0f);
            }
            SetCurve(clip, path, "m_LocalPosition.x", times, positions, 0);
            SetCurve(clip, path, "m_LocalPosition.y", times, positions, 1);
            SetCurve(clip, path, "m_LocalPosition.z", times, positions, 2);
        }

        private static void SetCurve(AnimationClip clip, string path, string property, float[] times, Vector3[] values, int axis)
        {
            var keys = new Keyframe[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                keys[i] = new Keyframe(times[i], values[i][axis]);
            }
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
        }

        // ---- Small helpers ----

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        private static Renderer Shape(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            Object.DestroyImmediate(shape.GetComponent<Collider>());
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = localPosition;
            shape.transform.localScale = scale;
            var renderer = shape.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static void SetArray(SerializedProperty array, Object[] items)
        {
            array.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }

        private static Material SaveMaterial(string name, Color color)
        {
            string path = $"{ArtFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                // A copy of the render pipeline's default material.
                GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                material = new Material(probe.GetComponent<Renderer>().sharedMaterial);
                Object.DestroyImmediate(probe);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
