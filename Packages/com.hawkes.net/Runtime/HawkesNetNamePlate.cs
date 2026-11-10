using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// World-space floating name above an avatar (quality rule 1 / text-facing): a soft backing plate and 3D text that
    /// copy the camera's rotation each <see cref="LateUpdate"/> (never <c>LookAt</c>, never a negative scale), scale up
    /// on a phone so they stay readable at 390 px, and hide when a wall sits between the camera and the plate.
    /// Built at run time (no TextMeshPro / uGUI package dependency): TextMesh + a translucent Quad; the demo WebGL
    /// build must keep <c>GUI/Text Shader</c> and <c>Sprites/Default</c> in Always Included Shaders.
    /// </summary>
    public sealed class HawkesNetNamePlate : MonoBehaviour
    {
        public const float HeadLift = 0.28f;
        public const float FallbackHeight = 2.05f;
        /// <summary>Target on-screen height of the name band as a fraction of the shorter screen edge (phone-readable).</summary>
        public const float ScreenHeightFrac = 0.045f;
        public const float PhoneScreenHeightFrac = 0.062f;

        static readonly List<HawkesNetNamePlate> All = new List<HawkesNetNamePlate>();
        static readonly StringBuilder ReportBuf = new StringBuilder(512);
        static Material sharedBack;
        static Font sharedFont;
        static int orbitMirroredFrames;
        static int orbitFrames;
        static bool orbitDone;
        static bool occlusionProbeDone;
        static bool occlusionHiddenOk;
        static bool occlusionShownOk;
        static string occlusionDetail = "";

        Transform avatarRoot;
        Transform face; // text + plate face the camera; local Y 180 so glyphs read on the camera-facing side
        TextMesh textMesh;
        Renderer backRenderer;
        Renderer textRenderer;
        bool occluded;
        float screenPx;
        bool mirrored;

        /// <summary>True when a wall (or other non-avatar collider) blocks the line from the camera to this plate.</summary>
        public bool Occluded => occluded;
        /// <summary>Estimated on-screen height of the name band in CSS/canvas pixels.</summary>
        public float ScreenPx => screenPx;
        /// <summary>True when this plate's world scale has a negative axis or its forward faces away from the camera.</summary>
        public bool Mirrored => mirrored;

        /// <summary>
        /// Attach a nameplate above the head bone (or <see cref="FallbackHeight"/> on the root). Reuses an existing
        /// plate on that avatar when present.
        /// </summary>
        public static HawkesNetNamePlate Attach(Transform avatarRoot, string label, Color tint)
        {
            if (!avatarRoot) return null;
            var existing = avatarRoot.GetComponentInChildren<HawkesNetNamePlate>(true);
            if (existing != null)
            {
                existing.Set(label, tint);
                return existing;
            }

            var head = FindHead(avatarRoot);
            var go = new GameObject("NamePlate");
            var plate = go.AddComponent<HawkesNetNamePlate>();
            plate.avatarRoot = avatarRoot;
            if (head != null)
            {
                go.transform.SetParent(head, false);
                go.transform.localPosition = Vector3.up * HeadLift;
            }
            else
            {
                go.transform.SetParent(avatarRoot, false);
                go.transform.localPosition = Vector3.up * FallbackHeight;
            }
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            plate.Build();
            plate.Set(label, tint);
            return plate;
        }

        public void Set(string label, Color tint)
        {
            if (textMesh == null) return;
            textMesh.text = string.IsNullOrEmpty(label) ? "Player" : label;
            // Soft tint toward the player's colour; stay bright enough over the dark plate.
            textMesh.color = Color.Lerp(Color.white, tint, 0.55f);
        }

        public static IReadOnlyList<HawkesNetNamePlate> Plates => All;

        /// <summary>True when every live nameplate has positive world scale on all axes (EditMode + playtest).</summary>
        public static bool AllScalesPositive(out string offender)
        {
            offender = null;
            foreach (var p in All)
            {
                if (!p || !p.isActiveAndEnabled) continue;
                if (HasNegativeScale(p.transform))
                {
                    offender = p.textMesh != null ? p.textMesh.text : p.name;
                    return false;
                }
            }
            // Also scan any TextMesh in the scene (EditMode builds plates, and catches hand-authored text).
            foreach (var tm in Object.FindObjectsOfType<TextMesh>())
            {
                if (HasNegativeScale(tm.transform))
                {
                    offender = tm.text;
                    return false;
                }
            }
            return true;
        }

        public static bool HasNegativeScale(Transform t)
        {
            while (t != null)
            {
                var s = t.localScale;
                if (s.x < 0f || s.y < 0f || s.z < 0f) return true;
                var ls = t.lossyScale;
                if (ls.x < 0f || ls.y < 0f || ls.z < 0f) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Playtest orbit: a dedicated disabled probe camera circles a target for one turn and counts frames where any
        /// nameplate would read mirrored. Does not move the gameplay follow cam (same lesson as the scene audit).
        /// </summary>
        public static IEnumerator OrbitAndCheck(Transform around, float radius, float height, float nearClip,
            System.Action onTick = null)
        {
            orbitDone = false;
            orbitFrames = 0;
            orbitMirroredFrames = 0;
            if (around == null) { orbitDone = true; yield break; }

            var probeGo = new GameObject("NamePlateOrbitProbe");
            var probe = probeGo.AddComponent<Camera>();
            probe.enabled = false;
            probe.nearClipPlane = Mathf.Max(nearClip, 0.1f);
            probe.clearFlags = CameraClearFlags.SolidColor;
            try
            {
                const float seconds = 4f;
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < seconds)
                {
                    float u = (Time.unscaledTime - t0) / seconds;
                    float ang = u * Mathf.PI * 2f;
                    Vector3 centre = around.position + Vector3.up * 1.2f;
                    probe.transform.position = centre + new Vector3(Mathf.Cos(ang) * radius, height, Mathf.Sin(ang) * radius);
                    probe.transform.LookAt(centre, Vector3.up);
                    // Drive every plate as if this were Camera.main for the facing test.
                    foreach (var p in All)
                        if (p) p.TickFacing(probe, writeBridge: false);
                    orbitFrames++;
                    foreach (var p in All)
                        if (p && p.isActiveAndEnabled && p.mirrored) { orbitMirroredFrames++; break; }
                    onTick?.Invoke();
                    Publish(probe);
                    yield return null;
                }
            }
            finally
            {
                if (probeGo) Object.Destroy(probeGo);
                orbitDone = true;
                Publish(Camera.main);
            }
        }

        /// <summary>
        /// Drop a temporary wall between the camera and <paramref name="plate"/>, assert it hides, remove it, assert it
        /// shows again. Results go into the names bridge for the playtest.
        /// </summary>
        public static IEnumerator ProbeOcclusion(HawkesNetNamePlate plate, Camera cam)
        {
            occlusionProbeDone = false;
            occlusionHiddenOk = false;
            occlusionShownOk = false;
            occlusionDetail = "";
            if (plate == null || cam == null)
            {
                occlusionDetail = "missing plate or camera";
                occlusionProbeDone = true;
                Publish(cam);
                yield break;
            }

            Vector3 from = cam.transform.position;
            Vector3 to = plate.transform.position;
            Vector3 mid = (from + to) * 0.5f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "NamePlateOccluder";
            wall.transform.position = mid;
            wall.transform.localScale = new Vector3(1.2f, 1.2f, 0.25f);
            wall.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
            // Keep the BoxCollider so Linecast hits it; mute the renderer so footage stays clean.
            var rend = wall.GetComponent<Renderer>();
            if (rend) rend.enabled = false;
            Physics.SyncTransforms();
            yield return null;
            plate.TickFacing(cam, writeBridge: false);
            occlusionHiddenOk = plate.occluded && !plate.IsDrawn;
            yield return null;
            Object.Destroy(wall);
            Physics.SyncTransforms();
            yield return null;
            plate.TickFacing(cam, writeBridge: false);
            occlusionShownOk = !plate.occluded && plate.IsDrawn;
            occlusionDetail = occlusionHiddenOk && occlusionShownOk
                ? "hid behind wall, shown when clear"
                : $"hiddenOk={occlusionHiddenOk} shownOk={occlusionShownOk} occluded={plate.occluded}";
            occlusionProbeDone = true;
            Publish(cam);
        }

        bool IsDrawn => textRenderer != null && textRenderer.enabled;

        void Build()
        {
            face = new GameObject("Face").transform;
            face.SetParent(transform, false);
            // Glyphs face local +Z; with parent = camera rotation, a 180° yaw makes them face the camera (no negative scale).
            face.localRotation = Quaternion.Euler(0f, 180f, 0f);
            face.localPosition = Vector3.zero;
            face.localScale = Vector3.one;

            if (sharedBack == null)
            {
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
                sharedBack = new Material(sh);
                sharedBack.color = new Color(0f, 0f, 0f, 0.55f);
            }
            if (sharedFont == null)
                sharedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            back.transform.SetParent(face, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            back.transform.localRotation = Quaternion.identity;
            back.transform.localScale = new Vector3(1.6f, 0.36f, 1f);
            Object.Destroy(back.GetComponent<Collider>());
            backRenderer = back.GetComponent<Renderer>();
            backRenderer.sharedMaterial = sharedBack;
            backRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backRenderer.receiveShadows = false;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(face, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0f);
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;
            textMesh = textGo.AddComponent<TextMesh>();
            textMesh.font = sharedFont;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.06f;
            textMesh.fontSize = 48;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.color = Color.white;
            if (sharedFont != null) textGo.GetComponent<MeshRenderer>().sharedMaterial = sharedFont.material;
            textRenderer = textGo.GetComponent<Renderer>();
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
        }

        void LateUpdate()
        {
            TickFacing(Camera.main, writeBridge: true);
        }

        void TickFacing(Camera cam, bool writeBridge)
        {
            if (!cam || textMesh == null) return;

            // Brief: copy the camera's rotation each LateUpdate (not LookAt, no negative scale).
            transform.rotation = cam.transform.rotation;
            // Keep every local scale positive (Face keeps Y=180 for glyph facing).
            transform.localScale = Vector3.one;

            float frac = HawkesNet.IsTouchDevice() ? PhoneScreenHeightFrac : ScreenHeightFrac;
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            float worldH = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * frac;
            worldH = Mathf.Clamp(worldH, 0.18f, 0.55f);
            // Face child's Quad height is 0.36 at scale 1; scale the root uniformly in world via parent lossy.
            float parentLossy = Mathf.Max(1e-4f, transform.parent != null ? transform.parent.lossyScale.y : 1f);
            float local = worldH / (0.36f * parentLossy);
            local = Mathf.Clamp(local, 0.35f, 3.5f);
            transform.localScale = new Vector3(local, local, local);

            // Measured on-screen height of the soft plate (uncheatable vs a fixed constant).
            if (backRenderer != null && backRenderer.enabled)
            {
                var b = backRenderer.bounds;
                var top = cam.WorldToScreenPoint(b.center + Vector3.up * b.extents.y);
                var bot = cam.WorldToScreenPoint(b.center - Vector3.up * b.extents.y);
                screenPx = top.z > 0f && bot.z > 0f ? Mathf.Abs(top.y - bot.y) : frac * Mathf.Min(Screen.width, Screen.height);
            }
            else
                screenPx = frac * Mathf.Min(Screen.width, Screen.height);

            // Occlusion: a wall or prop between the camera and the plate. Skip avatar CapsuleColliders
            // (soft push-apart), the ground plane, and nameplate quads so remotes stay visible in the open.
            Vector3 eye = cam.transform.position;
            Vector3 tip = transform.position;
            occluded = false;
            var hits = Physics.RaycastAll(eye, (tip - eye).normalized, Vector3.Distance(eye, tip), ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!hit.collider) continue;
                if (hit.collider is CapsuleCollider) continue;
                var t = hit.transform;
                if (t != null && avatarRoot != null && (t == avatarRoot || t.IsChildOf(avatarRoot))) continue;
                if (t != null && t.GetComponentInParent<HawkesNetNamePlate>() != null) continue;
                if (t != null && t.name == "Ground") continue;
                occluded = true;
                break;
            }

            bool inFront = Vector3.Dot(cam.transform.forward, (tip - eye).normalized) > 0.02f;
            bool show = !occluded && inFront;
            if (backRenderer) backRenderer.enabled = show;
            if (textRenderer) textRenderer.enabled = show;

            mirrored = HasNegativeScale(transform) || Vector3.Dot(transform.forward, cam.transform.forward) < 0.15f;
            // With camera-copy rotation, plate.forward == cam.forward (Dot ≈ 1). Facing away would mean mirrored.

            if (writeBridge) Publish(cam);
        }

        static Transform FindHead(Transform root)
        {
            if (!root) return null;
            var anim = HawkesNetAvatarVisual.GetAnimator(root);
            if (anim != null && anim.isHuman)
            {
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (head) return head;
            }
            // RobotExpressive / glTF often keeps a "Head" transform even on a Generic rig.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                var n = t.name;
                if (n == "Head" || n == "head" || n.EndsWith("_Head") || n.EndsWith(":Head")) return t;
            }
            return null;
        }

        /// <summary>JSON for <c>window.__hawkesNet.names</c>.</summary>
        public static string ToJson(Camera cam = null)
        {
            var inv = CultureInfo.InvariantCulture;
            ReportBuf.Length = 0;
            ReportBuf.Append("{\"orbitDone\":").Append(orbitDone ? "true" : "false")
                .Append(",\"orbitFrames\":").Append(orbitFrames)
                .Append(",\"mirroredFrames\":").Append(orbitMirroredFrames)
                .Append(",\"occlusionDone\":").Append(occlusionProbeDone ? "true" : "false")
                .Append(",\"occlusionHiddenOk\":").Append(occlusionHiddenOk ? "true" : "false")
                .Append(",\"occlusionShownOk\":").Append(occlusionShownOk ? "true" : "false")
                .Append(",\"occlusionDetail\":\"").Append((occlusionDetail ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"')
                .Append(",\"phone\":").Append(HawkesNet.IsTouchDevice() ? "true" : "false")
                .Append(",\"plates\":[");
            bool first = true;
            foreach (var p in All)
            {
                if (!p || !p.isActiveAndEnabled) continue;
                if (!first) ReportBuf.Append(',');
                first = false;
                var s = p.transform.lossyScale;
                ReportBuf.Append("{\"text\":\"").Append((p.textMesh != null ? p.textMesh.text : "").Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\",\"occluded\":").Append(p.occluded ? "true" : "false")
                    .Append(",\"shown\":").Append(p.IsDrawn ? "true" : "false")
                    .Append(",\"mirrored\":").Append(p.mirrored ? "true" : "false")
                    .Append(",\"screenPx\":").Append(p.screenPx.ToString("0.0", inv))
                    .Append(",\"scale\":[").Append(s.x.ToString("0.###", inv)).Append(',')
                    .Append(s.y.ToString("0.###", inv)).Append(',')
                    .Append(s.z.ToString("0.###", inv)).Append(']')
                    .Append('}');
            }
            ReportBuf.Append("]}");
            return ReportBuf.ToString();
        }

        static void Publish(Camera cam)
        {
            HawkesNet.ShowedNames(ToJson(cam));
        }
    }
}
