using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Shared look for the Avatars sample and the demo: a tinted RobotExpressive humanoid (or a capsule
    /// stand-in when the baked Resources prefab is missing). The Editor bake
    /// (<c>HawkesNetAvatarBake</c>) writes <c>Resources/HawkesNetAvatar</c> with an Animator whose 1D
    /// blend thresholds come from each clip's root speed; call <see cref="Tick"/> and <see cref="PlayEmote"/>
    /// each frame / on emote.
    /// </summary>
    public static class HawkesNetAvatarVisual
    {
        public const string ResourceName = "HawkesNetAvatar";
        public const float LabelHeight = 2f;
        /// <summary>Nominal walk/run speeds (m/s) matching the baked blend thresholds when root motion is absent.</summary>
        public static float WalkNominal = 1.2f;
        public static float RunNominal = 3.5f;

        /// <summary>Motor on the baked prefab: Animator plus the walk/run thresholds the controller was built with.</summary>
        public sealed class Motor : MonoBehaviour
        {
            public Animator Animator;
            public float WalkSpeed = 1.2f;
            public float RunSpeed = 3.5f;
        }

        /// <summary>
        /// An empty root (what HawkesNetAvatars moves) with a "Body" child that holds the humanoid (or capsule)
        /// and a CapsuleCollider on the root for soft push-apart / WebGL physics stripping.
        /// Feet sit at local y=0. Tint uses a MaterialPropertyBlock.
        /// </summary>
        public static Transform Make(string name, Color color)
        {
            var root = new GameObject(name).transform;
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);

            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab != null)
            {
                var visual = Object.Instantiate(prefab, body, false);
                visual.name = "Humanoid";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                FitFeetToGround(visual.transform);
                SizeCollider(root.gameObject, visual.transform);
                var motor = visual.GetComponent<Motor>() ?? visual.GetComponentInChildren<Motor>();
                if (motor != null)
                {
                    if (motor.WalkSpeed > 0.01f) WalkNominal = motor.WalkSpeed;
                    if (motor.RunSpeed > 0.01f) RunNominal = motor.RunSpeed;
                    if (motor.Animator == null) motor.Animator = visual.GetComponentInChildren<Animator>();
                }
                // Playtest / console: prove the baked humanoid loaded (not the capsule stand-in).
                Debug.Log("[HawkesNetAvatar] humanoid ready");
            }
            else
            {
                Debug.LogWarning("[Hawkes] HawkesNetAvatar prefab missing (run Hawkes Net > Bake Avatars humanoid). Using a capsule stand-in.");
                Debug.Log("[HawkesNetAvatar] capsule stand-in");
                CapsuleStandIn(body, color);
                var col = root.gameObject.AddComponent<CapsuleCollider>();
                col.height = 2f;
                col.radius = 0.5f;
                col.center = new Vector3(0f, 1f, 0f);
                // Naming CapsuleCollider keeps the Physics module in WebGL builds that strip unused engine code.
                _ = typeof(CapsuleCollider);
            }

            if (root.GetComponent<CapsuleCollider>() == null)
            {
                var col = root.gameObject.AddComponent<CapsuleCollider>();
                col.height = 1.8f;
                col.radius = 0.35f;
                col.center = new Vector3(0f, 0.9f, 0f);
            }

            SetTint(root, color);
            return root;
        }

        public static Animator GetAnimator(Transform root)
        {
            if (!root) return null;
            var motor = root.GetComponentInChildren<Motor>();
            if (motor != null && motor.Animator) return motor.Animator;
            return root.GetComponentInChildren<Animator>();
        }

        public static void SetTint(Transform root, Color color)
        {
            if (!root) return;
            var block = new MaterialPropertyBlock();
            // glTFast / Built-in / URP property names — set several so tint shows on whatever shader the bake used.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_MainColor", color);
                block.SetColor("baseColorFactor", color);
                r.SetPropertyBlock(block);
                // Also nudge the shared material colour when the shader exposes .color (Built-in Standard).
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m) continue;
                    if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                }
            }
        }

        /// <summary>Drive the locomotion blend from ground speed (m/s) and scale playback to match (quality rule 5).</summary>
        public static void Tick(Animator anim, float groundSpeed)
        {
            if (!anim || !anim.runtimeAnimatorController) return;
            float speed = Mathf.Max(0f, groundSpeed);
            anim.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            if (speed < 0.05f)
            {
                anim.speed = 1f;
                return;
            }
            float walk = WalkNominal, run = RunNominal;
            float nominal = speed < (walk + run) * 0.5f ? Mathf.Max(walk, 0.01f) : Mathf.Max(run, 0.01f);
            anim.speed = Mathf.Clamp(speed / nominal, 0.5f, 2f);
        }

        public static void PlayEmote(Animator anim, int emote)
        {
            if (!anim || !anim.runtimeAnimatorController) return;
            switch (emote)
            {
                case 1: anim.SetTrigger(WaveHash); break;
                case 2: anim.SetTrigger(HopHash); break;
                case 3: anim.SetTrigger(SpinHash); break;
            }
        }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int WaveHash = Animator.StringToHash("Wave");
        static readonly int HopHash = Animator.StringToHash("Hop");
        static readonly int SpinHash = Animator.StringToHash("Spin");

        static void CapsuleStandIn(Transform body, Color color)
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Humanoid";
            Object.Destroy(capsule.GetComponent<CapsuleCollider>());
            capsule.transform.SetParent(body, false);
            capsule.transform.localPosition = new Vector3(0f, 1f, 0f);
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { color = color };
            capsule.GetComponent<Renderer>().sharedMaterial = mat;
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Object.Destroy(nose.GetComponent<BoxCollider>());
            nose.transform.SetParent(capsule.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.55f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
            nose.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = new Color(0.12f, 0.12f, 0.15f) };
        }

        /// <summary>Shift the visual so its lowest rendered point sits at local y=0 (no floating or sinking).</summary>
        public static void FitFeetToGround(Transform visual)
        {
            if (!visual) return;
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            // bounds are in world space; visual is typically at identity under Body
            float minY = bounds.min.y - visual.position.y;
            visual.localPosition = new Vector3(visual.localPosition.x, visual.localPosition.y - minY, visual.localPosition.z);
            // Cap height ~1.8 m for the arena camera
            float height = bounds.size.y;
            if (height > 0.05f && (height < 1.2f || height > 2.2f))
            {
                float s = 1.8f / height;
                visual.localScale *= s;
                renderers = visual.GetComponentsInChildren<Renderer>();
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                minY = bounds.min.y - visual.position.y;
                visual.localPosition = new Vector3(visual.localPosition.x, visual.localPosition.y - minY, visual.localPosition.z);
            }
        }

        static void SizeCollider(GameObject root, Transform visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float height = Mathf.Max(0.5f, bounds.size.y);
            float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.2f, 0.5f);
            var col = root.GetComponent<CapsuleCollider>() ?? root.AddComponent<CapsuleCollider>();
            col.height = height;
            col.radius = radius;
            col.center = new Vector3(0f, height * 0.5f, 0f);
            _ = typeof(CapsuleCollider);
            _ = typeof(MeshCollider); // keep Physics module referenced for WebGL stripping checks
        }
    }
}
