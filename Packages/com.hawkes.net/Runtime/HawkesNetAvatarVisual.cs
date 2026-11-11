using System.Globalization;
using System.Text;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Shared look for the Avatars sample and the demo: a tinted RobotExpressive humanoid (or a capsule
    /// stand-in when the baked Resources prefab is missing). The Editor bake
    /// (<c>HawkesNetAvatarBake</c>) writes <c>Resources/HawkesNetAvatar</c> with an Animator whose 1D
    /// blend thresholds come from each clip's root speed, Jump → Land → Locomotion, and Wave/Hop/Spin;
    /// call <see cref="Tick"/>, <see cref="PlayEmote"/> and <see cref="PlayJump"/> each frame / on input.
    /// </summary>
    public static class HawkesNetAvatarVisual
    {
        public const string ResourceName = "HawkesNetAvatar";
        public const float LabelHeight = 2f;
        /// <summary>Nominal walk/run speeds (m/s) matching the baked blend thresholds when root motion is absent.</summary>
        public static float WalkNominal = 1.2f;
        public static float RunNominal = 3.5f;
        public const float JumpSpeed = 5.5f;
        public const float Gravity = 18f;
        /// <summary>How long a land clip may run before returning to locomotion (quality rule 5: ≤ 0.3 s).</summary>
        public const float LandMaxSeconds = 0.3f;

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
                var motor = visual.GetComponent<HawkesNetAvatarMotor>() ?? visual.GetComponentInChildren<HawkesNetAvatarMotor>();
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
            var motor = root.GetComponentInChildren<HawkesNetAvatarMotor>();
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

        /// <summary>
        /// Capsule ground probe under the avatar root: SphereCast from just above the feet. Hits slopes and steps.
        /// Returns true when the feet are within <paramref name="skin"/> of the hit (grounded).
        /// </summary>
        public static bool ProbeGround(Transform root, out float groundY, out Vector3 normal, float skin = 0.12f)
        {
            groundY = root ? root.position.y : 0f;
            normal = Vector3.up;
            if (!root) return false;
            var col = root.GetComponent<CapsuleCollider>();
            float radius = col != null ? Mathf.Max(0.12f, col.radius * 0.85f) : 0.28f;
            float height = col != null ? Mathf.Max(col.height, radius * 2f) : 1.8f;
            // Cast from mid-shin so a step under the feet still hits; skin lets a slope count as grounded.
            Vector3 origin = root.position + Vector3.up * (radius + 0.05f);
            float maxDist = height * 0.55f + skin;
            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, maxDist, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit? best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                // Ignore this avatar's own capsule (and children): the cast starts inside it.
                if (!h.collider || h.transform == root || h.transform.IsChildOf(root)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; }
            }
            if (best == null)
            {
                // Flat arena fallback when colliders were stripped: treat y≈0 as ground under the feet.
                groundY = 0f;
                return root.position.y <= skin;
            }
            groundY = best.Value.point.y;
            normal = best.Value.normal;
            float gap = root.position.y - groundY;
            return gap <= skin && best.Value.normal.y > 0.35f;
        }

        /// <summary>Drive the locomotion blend from ground speed (m/s), grounded flag, and scale playback (quality rule 5).</summary>
        public static void Tick(Animator anim, float groundSpeed, bool grounded = true)
        {
            if (!anim || !anim.runtimeAnimatorController) return;
            float speed = Mathf.Max(0f, groundSpeed);
            if (HasParam(anim, GroundedHash)) anim.SetBool(GroundedHash, grounded);
            anim.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            // Don't retune playback while jumping / landing — keep the clip at authored speed.
            if (IsAirborneState(anim) || IsLandState(anim))
            {
                anim.speed = 1f;
                return;
            }
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

        /// <summary>Start a Jump → Land → Locomotion takeoff (Space / synced jump count).</summary>
        public static void PlayJump(Animator anim)
        {
            if (!anim || !anim.runtimeAnimatorController) return;
            if (HasParam(anim, JumpHash)) anim.SetTrigger(JumpHash);
            else anim.SetTrigger(HopHash); // older bake: Hop used the Jump clip
            if (HasParam(anim, GroundedHash)) anim.SetBool(GroundedHash, false);
        }

        public static bool IsAirborneState(Animator anim)
        {
            if (!anim || !anim.isInitialized) return false;
            // Only the physics Jump state — Hop is the in-place emote (key 2) and stays grounded.
            return anim.GetCurrentAnimatorStateInfo(0).IsName("Jump");
        }

        public static bool IsLandState(Animator anim)
        {
            if (!anim || !anim.isInitialized) return false;
            return anim.GetCurrentAnimatorStateInfo(0).IsName("Land");
        }

        public static string StateName(Animator anim)
        {
            if (!anim || !anim.isInitialized) return "";
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Locomotion")) return "Locomotion";
            if (info.IsName("Jump")) return "Jump";
            if (info.IsName("Land")) return "Land";
            if (info.IsName("Wave")) return "Wave";
            if (info.IsName("Hop")) return "Hop";
            if (info.IsName("Spin")) return "Spin";
            return "Other";
        }

        /// <summary>
        /// Planted-foot slide speed (m/s): how fast the root moves along the ground while the Animator is idle
        /// (Speed near 0) or landing. Zero when airborne or when locomotion speed matches travel.
        /// </summary>
        public static float FootSlideSpeed(Animator anim, float groundSpeed, bool grounded)
        {
            if (!grounded) return 0f;
            if (anim && anim.runtimeAnimatorController)
            {
                float blend = anim.GetFloat(SpeedHash);
                if (IsLandState(anim)) return groundSpeed;
                if (blend < 0.15f && groundSpeed > 0.05f) return groundSpeed;
                // Moving much faster than the blend implies (e.g. Idle while sliding).
                float expect = blend;
                if (groundSpeed > expect + 1.2f) return groundSpeed - expect;
                return 0f;
            }
            return 0f;
        }

        /// <summary>True when the Animator looks airborne (Jump) while the probe says grounded for too long.</summary>
        public static bool StuckAirborne(Animator anim, bool grounded, float airborneWhileGroundedSeconds)
            => grounded && IsAirborneState(anim) && airborneWhileGroundedSeconds > LandMaxSeconds;

        /// <summary>State/motion mismatch: idle blend while racing, or run blend while still, or Jump while grounded.</summary>
        public static bool StateMotionMismatch(Animator anim, float groundSpeed, bool grounded)
        {
            if (!anim || !anim.runtimeAnimatorController) return false;
            float blend = anim.GetFloat(SpeedHash);
            if (grounded && IsAirborneState(anim)) return true;
            if (groundSpeed > 2f && blend < 0.2f && !IsAirborneState(anim) && !IsLandState(anim)) return true;
            if (groundSpeed < 0.1f && blend > 2f && grounded) return true;
            return false;
        }

        /// <summary>One character's quality sample for the playtest bridge (<c>window.__hawkesNet.anim</c>).</summary>
        public static void AppendAnimSample(StringBuilder b, string id, bool grounded, float groundSpeed, float slide, float airWhileGrounded, Animator anim, bool local)
        {
            var inv = CultureInfo.InvariantCulture;
            if (b.Length > 1 && b[b.Length - 1] != '[') b.Append(',');
            string st = StateName(anim);
            int air = IsAirborneState(anim) ? 1 : 0;
            int land = IsLandState(anim) ? 1 : 0;
            int mismatch = StateMotionMismatch(anim, groundSpeed, grounded) ? 1 : 0;
            int stuck = StuckAirborne(anim, grounded, airWhileGrounded) ? 1 : 0;
            b.Append("{\"id\":\"").Append(id).Append("\",\"loc\":").Append(local ? 1 : 0)
                .Append(",\"g\":").Append(grounded ? 1 : 0)
                .Append(",\"air\":").Append(air)
                .Append(",\"land\":").Append(land)
                .Append(",\"st\":\"").Append(st).Append('"')
                .Append(",\"spd\":").Append(groundSpeed.ToString("0.###", inv))
                .Append(",\"slide\":").Append(slide.ToString("0.####", inv))
                .Append(",\"airG\":").Append(airWhileGrounded.ToString("0.###", inv))
                .Append(",\"mis\":").Append(mismatch)
                .Append(",\"stuck\":").Append(stuck)
                .Append('}');
        }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int WaveHash = Animator.StringToHash("Wave");
        static readonly int HopHash = Animator.StringToHash("Hop");
        static readonly int SpinHash = Animator.StringToHash("Spin");

        static bool HasParam(Animator anim, int hash)
        {
            if (!anim) return false;
            foreach (var p in anim.parameters)
                if (p.nameHash == hash) return true;
            return false;
        }

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
