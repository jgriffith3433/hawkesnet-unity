#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hawkes.Editor
{
    /// <summary>
    /// Builds the Avatars humanoid Resources prefab (RobotExpressive + Animator controller) for the sample and the
    /// demo. Called from <see cref="Hawkes.Demo.Editor.HawkesNetDemoBuild"/> before a WebGL build (InitializeOnLoad is
    /// not enough in cloud batchmode). Menu: Hawkes Net > Bake Avatars humanoid.
    /// </summary>
    public static class HawkesNetAvatarBake
    {
        public const string DemoGlbPath = "Assets/HawkesNetDemo/Models/RobotExpressive.glb";
        public const string SampleGlbPath = "Packages/com.hawkes.net/Samples~/Avatars/Models/RobotExpressive.glb";
        public const string ResourcesDir = "Packages/com.hawkes.net/Runtime/Resources";
        public const string PrefabPath = ResourcesDir + "/HawkesNetAvatar.prefab";
        public const string ControllerPath = ResourcesDir + "/HawkesNetAvatar.controller";

        [MenuItem("Hawkes Net/Bake Avatars humanoid")]
        public static void BakeMenu()
        {
            if (!Ensure()) throw new System.Exception("HawkesNetAvatarBake failed (see console)");
            EditorUtility.DisplayDialog("Hawkes Net", "Baked Resources/HawkesNetAvatar (humanoid + Animator).", "OK");
        }

        /// <summary>
        /// Resolve RobotExpressive.glb: demo Models, an imported Avatars sample under Assets/Samples, or the package
        /// Samples~ copy (when Unity can see it). Prefer a path AssetDatabase already knows.
        /// </summary>
        public static string FindGlbPath()
        {
            foreach (var p in new[] { DemoGlbPath, SampleGlbPath })
                if (File.Exists(p)) return p;
            foreach (var guid in AssetDatabase.FindAssets("RobotExpressive"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase) && File.Exists(p)) return p;
            }
            // Package Manager sample import lands under Assets/Samples/.../Avatars/Models/
            var hits = Directory.Exists("Assets/Samples")
                ? Directory.GetFiles("Assets/Samples", "RobotExpressive.glb", SearchOption.AllDirectories)
                : System.Array.Empty<string>();
            return hits.Length > 0 ? hits[0].Replace('\\', '/') : null;
        }

        /// <summary>Import the glb, build the Animator controller (1D blend from each clip's root speed) and prefab. Returns false on failure.</summary>
        public static bool Ensure()
        {
            var glbPath = FindGlbPath();
            if (string.IsNullOrEmpty(glbPath) || !File.Exists(glbPath))
            {
                Debug.LogError("[Hawkes] RobotExpressive.glb missing (expected " + DemoGlbPath + " or the Avatars sample Models/).");
                return false;
            }

            Directory.CreateDirectory(ResourcesDir);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (model == null)
            {
                Debug.LogError("[Hawkes] Could not load " + glbPath + " (is com.unity.cloud.gltfast installed?)");
                return false;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview__", System.StringComparison.Ordinal))
                .ToList();
            if (clips.Count == 0)
            {
                Debug.LogError("[Hawkes] No AnimationClips on " + glbPath + "; glTFast import may have failed.");
                return false;
            }

            var idle = FindClip(clips, "Idle");
            var walk = FindClip(clips, "Walking", "Walk");
            var run = FindClip(clips, "Running", "Run");
            var jump = FindClip(clips, "Jump");
            var wave = FindClip(clips, "Wave");
            var dance = FindClip(clips, "Dance");
            if (idle == null || walk == null || run == null || jump == null || wave == null || dance == null)
            {
                Debug.LogError("[Hawkes] RobotExpressive is missing Idle/Walking/Running/Jump/Wave/Dance. Found: "
                    + string.Join(", ", clips.Select(c => c.name)));
                return false;
            }

            float walkSpeed = NominalSpeed(walk, 1.2f);
            float runSpeed = NominalSpeed(run, 3.5f);
            if (runSpeed <= walkSpeed) runSpeed = walkSpeed + 1.5f;

            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            if (File.Exists(PrefabPath)) AssetDatabase.DeleteAsset(PrefabPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            // CreateAnimatorControllerAtPath adds a Speed float and a default blend tree — rebuild cleanly.
            while (controller.parameters.Length > 0)
                controller.RemoveParameter(0);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Wave", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hop", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Spin", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            foreach (var cs in sm.states.ToArray()) sm.RemoveState(cs.state);
            foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);

            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, walkSpeed);
            tree.AddChild(run, runSpeed);

            var loco = sm.AddState("Locomotion", new Vector3(300f, 0f, 0f));
            loco.motion = tree;
            sm.defaultState = loco;

            AddEmoteState(sm, loco, "Wave", wave, "Wave", new Vector3(300f, 80f, 0f));
            AddEmoteState(sm, loco, "Hop", jump, "Hop", new Vector3(300f, 160f, 0f));
            AddEmoteState(sm, loco, "Spin", dance, "Spin", new Vector3(300f, 240f, 0f));
            // Locomotion exits into emotes through the Any State transitions above; every emote state exits back
            // to Locomotion (and also has an Exit transition) so no state is a dead end.

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var instance = Object.Instantiate(model);
            instance.name = "HawkesNetAvatar";
            // Prefer Mecanim Animator on the imported root; add one if the importer left Legacy only.
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller;

            // Drop Legacy Animation if present so only the Animator drives the figure.
            foreach (var legacy in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(legacy);

            HawkesNetAvatarVisual.FitFeetToGround(instance.transform);

            var motor = instance.GetComponent<HawkesNetAvatarVisual.Motor>()
                ?? instance.AddComponent<HawkesNetAvatarVisual.Motor>();
            motor.Animator = animator;
            motor.WalkSpeed = walkSpeed;
            motor.RunSpeed = runSpeed;

            // Outward normals / positive scale (quality rules 3–4).
            var scale = instance.transform.localScale;
            if (scale.x < 0f || scale.y < 0f || scale.z < 0f)
                instance.transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Hawkes] Baked {PrefabPath} (walk={walkSpeed:0.##} m/s, run={runSpeed:0.##} m/s, clips={clips.Count})");
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        }

        static void AddEmoteState(AnimatorStateMachine sm, AnimatorState loco, string stateName, AnimationClip clip, string trigger, Vector3 pos)
        {
            var st = sm.AddState(stateName, pos);
            st.motion = clip;
            var enter = sm.AddAnyStateTransition(st);
            enter.hasExitTime = false;
            enter.duration = 0.05f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            var back = st.AddTransition(loco);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.15f;
            back.hasFixedDuration = true;
            // Explicit exit as well (quality: every state has an exit).
            var exit = st.AddExitTransition();
            exit.hasExitTime = true;
            exit.exitTime = 1f;
            exit.duration = 0f;
        }

        static AnimationClip FindClip(List<AnimationClip> clips, params string[] names)
        {
            foreach (var name in names)
            {
                var hit = clips.FirstOrDefault(c => string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            foreach (var name in names)
            {
                var hit = clips.FirstOrDefault(c => c.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Clip root speed (m/s); falls back to a nominal when the clip is in-place (RobotExpressive).</summary>
        static float NominalSpeed(AnimationClip clip, float fallback)
        {
            if (clip == null) return fallback;
            float s = clip.averageSpeed.magnitude;
            if (s > 0.05f) return s;
            return fallback;
        }
    }
}
#endif
