using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Scene quality audit for the Avatars sample / demo (game quality rules 2, 3, 4, 11): static props sit on the
    /// ground (±2 cm), no coplanar z-fighting pairs, no inside-out meshes, camera near clip ≥ 0.1, and a scripted
    /// orbit never puts the camera inside a static collider. Results go to the page via <see cref="HawkesNet.ShowedScene"/>.
    /// </summary>
    public static class HawkesNetSceneAudit
    {
        public const float GroundTolerance = 0.02f;
        public const float ZFightGap = 0.001f;
        public const float MinNearClip = 0.1f;
        public const float OrbitSeconds = 20f;

        /// <summary>One finished (or in-progress) audit for the playtest bridge.</summary>
        public sealed class Report
        {
            public readonly List<string> Floating = new List<string>();
            public readonly List<string> Sunk = new List<string>();
            public readonly List<string> ZFight = new List<string>();
            public readonly List<string> InsideOut = new List<string>();
            public readonly List<string> CameraInside = new List<string>();
            public float NearClip;
            public bool NearClipOk;
            public int OrbitFrames;
            public int CameraInsideFrames;
            public bool StaticDone;
            public bool Done;

            public bool Clean =>
                Floating.Count == 0 && Sunk.Count == 0 && ZFight.Count == 0 && InsideOut.Count == 0
                && CameraInsideFrames == 0 && NearClipOk;
        }

        /// <summary>True for arena dressing the audit cares about (not avatars or the ground plane itself).</summary>
        public static bool IsStaticProp(Renderer r)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            var n = r.gameObject.name;
            if (n == "Ground" || n == "Player" || n.StartsWith("Remote", StringComparison.Ordinal)) return false;
            if (n == "NamePlateOccluder" || n == "Back" || n == "Text" || n == "Face" || n == "NamePlate") return false;
            if (r.GetComponentInParent<HawkesNetNamePlate>() != null) return false;
            if (r.GetComponentInParent<HawkesNetAvatars>() != null && r.GetComponent<MeshFilter>() == null) return false;
            // Skip skinned avatar meshes under the local/remote roots.
            if (r is SkinnedMeshRenderer) return false;
            return r.GetComponent<MeshFilter>() != null;
        }

        /// <summary>Raycast ground contact, z-fight pairs, inside-out / negative scale, and near clip.</summary>
        public static void AuditStatic(Report report, Camera cam = null)
        {
            if (report == null) return;
            report.Floating.Clear();
            report.Sunk.Clear();
            report.ZFight.Clear();
            report.InsideOut.Clear();

            cam = cam != null ? cam : Camera.main;
            if (cam != null)
            {
                report.NearClip = cam.nearClipPlane;
                report.NearClipOk = cam.nearClipPlane + 1e-6f >= MinNearClip;
            }
            else
            {
                report.NearClip = 0f;
                report.NearClipOk = false;
            }

            Physics.SyncTransforms();
            var props = new List<Renderer>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (IsStaticProp(r)) props.Add(r);

            foreach (var r in props)
            {
                CheckGround(r, report);
                CheckInsideOut(r, report);
            }
            CheckZFight(props, report);
            report.StaticDone = true;
        }

        static void CheckGround(Renderer r, Report report)
        {
            var b = r.bounds;
            // Start just above the bounds bottom so we do not hit this prop's own collider first.
            var origin = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, c) => a.distance.CompareTo(c.distance));
            RaycastHit? ground = null;
            foreach (var h in hits)
            {
                if (h.collider != null && h.collider.transform.IsChildOf(r.transform)) continue;
                if (h.collider != null && (h.collider.transform == r.transform || h.collider.gameObject == r.gameObject)) continue;
                ground = h;
                break;
            }
            if (ground == null)
            {
                // No support under it (off the arena): treat as floating from y=0.
                float gap = b.min.y;
                if (gap > GroundTolerance) report.Floating.Add(r.gameObject.name);
                else if (gap < -GroundTolerance) report.Sunk.Add(r.gameObject.name);
                return;
            }
            float off = b.min.y - ground.Value.point.y;
            if (off > GroundTolerance) report.Floating.Add(r.gameObject.name);
            else if (off < -GroundTolerance) report.Sunk.Add(r.gameObject.name);
        }

        static void CheckInsideOut(Renderer r, Report report)
        {
            var t = r.transform;
            var s = t.lossyScale;
            if (s.x * s.y * s.z < 0f)
            {
                report.InsideOut.Add(r.gameObject.name);
                return;
            }
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mesh.vertexCount < 3 || !mesh.isReadable) return;
            var norms = mesh.normals;
            var verts = mesh.vertices;
            if (norms == null || norms.Length != verts.Length) return;
            var tris = mesh.triangles;
            if (tris == null || tris.Length < 3) return;
            int against = 0, counted = 0;
            var center = mesh.bounds.center;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (a >= norms.Length || b >= norms.Length || c >= norms.Length) continue;
                var n = (norms[a] + norms[b] + norms[c]).normalized;
                var face = (verts[a] + verts[b] + verts[c]) / 3f;
                var outward = (face - center).normalized;
                if (outward.sqrMagnitude < 1e-8f) continue;
                counted++;
                if (Vector3.Dot(n, outward) < 0f) against++;
            }
            if (counted > 0 && against > counted * 0.6f) report.InsideOut.Add(r.gameObject.name);
        }

        static void CheckZFight(List<Renderer> props, Report report)
        {
            for (int i = 0; i < props.Count; i++)
            {
                var a = props[i];
                var ba = a.bounds;
                for (int j = i + 1; j < props.Count; j++)
                {
                    var b = props[j];
                    var bb = b.bounds;
                    if (!ba.Intersects(bb)) continue;
                    // Coplanar overlap: shared face plane within 1 mm and projected footprints overlap.
                    if (FacesCoplanarOverlap(ba, bb))
                        report.ZFight.Add(a.gameObject.name + "|" + b.gameObject.name);
                }
            }
        }

        static bool FacesCoplanarOverlap(Bounds a, Bounds b)
        {
            // Axis-aligned face pairs (props are cubes/planes): same plane within ZFightGap and overlap on the other two axes.
            if (Mathf.Abs(a.max.y - b.min.y) <= ZFightGap || Mathf.Abs(b.max.y - a.min.y) <= ZFightGap || Mathf.Abs(a.min.y - b.min.y) <= ZFightGap && Mathf.Abs(a.max.y - b.max.y) <= ZFightGap)
                if (Overlap1D(a.min.x, a.max.x, b.min.x, b.max.x) && Overlap1D(a.min.z, a.max.z, b.min.z, b.max.z))
                    // Thin slabs that only touch at an edge are fine; require area overlap > a few cm².
                    if (OverlapLen(a.min.x, a.max.x, b.min.x, b.max.x) > 0.05f && OverlapLen(a.min.z, a.max.z, b.min.z, b.max.z) > 0.05f)
                        return CoplanarY(a, b);
            if (Mathf.Abs(a.max.x - b.min.x) <= ZFightGap || Mathf.Abs(b.max.x - a.min.x) <= ZFightGap || Mathf.Abs(a.min.x - b.min.x) <= ZFightGap && Mathf.Abs(a.max.x - b.max.x) <= ZFightGap)
                if (Overlap1D(a.min.y, a.max.y, b.min.y, b.max.y) && Overlap1D(a.min.z, a.max.z, b.min.z, b.max.z))
                    if (OverlapLen(a.min.y, a.max.y, b.min.y, b.max.y) > 0.05f && OverlapLen(a.min.z, a.max.z, b.min.z, b.max.z) > 0.05f)
                        return CoplanarX(a, b);
            if (Mathf.Abs(a.max.z - b.min.z) <= ZFightGap || Mathf.Abs(b.max.z - a.min.z) <= ZFightGap || Mathf.Abs(a.min.z - b.min.z) <= ZFightGap && Mathf.Abs(a.max.z - b.max.z) <= ZFightGap)
                if (Overlap1D(a.min.x, a.max.x, b.min.x, b.max.x) && Overlap1D(a.min.y, a.max.y, b.min.y, b.max.y))
                    if (OverlapLen(a.min.x, a.max.x, b.min.x, b.max.x) > 0.05f && OverlapLen(a.min.y, a.max.y, b.min.y, b.max.y) > 0.05f)
                        return CoplanarZ(a, b);
            return false;
        }

        static bool CoplanarY(Bounds a, Bounds b) =>
            Mathf.Abs(a.max.y - b.min.y) <= ZFightGap || Mathf.Abs(b.max.y - a.min.y) <= ZFightGap
            || (Mathf.Abs(a.min.y - b.min.y) <= ZFightGap && Mathf.Abs(a.size.y) <= 0.02f && Mathf.Abs(b.size.y) <= 0.02f);

        static bool CoplanarX(Bounds a, Bounds b) =>
            Mathf.Abs(a.max.x - b.min.x) <= ZFightGap || Mathf.Abs(b.max.x - a.min.x) <= ZFightGap
            || (Mathf.Abs(a.min.x - b.min.x) <= ZFightGap && Mathf.Abs(a.size.x) <= 0.02f && Mathf.Abs(b.size.x) <= 0.02f);

        static bool CoplanarZ(Bounds a, Bounds b) =>
            Mathf.Abs(a.max.z - b.min.z) <= ZFightGap || Mathf.Abs(b.max.z - a.min.z) <= ZFightGap
            || (Mathf.Abs(a.min.z - b.min.z) <= ZFightGap && Mathf.Abs(a.size.z) <= 0.02f && Mathf.Abs(b.size.z) <= 0.02f);

        static bool Overlap1D(float a0, float a1, float b0, float b1) => a0 < b1 - 1e-5f && b0 < a1 - 1e-5f;
        static float OverlapLen(float a0, float a1, float b0, float b1) => Mathf.Max(0f, Mathf.Min(a1, b1) - Mathf.Max(a0, b0));

        /// <summary>Whether the camera point sits inside any static mesh/box collider (not avatars).</summary>
        public static bool CameraInsideStatic(Vector3 pos, out string name)
        {
            name = null;
            var hits = Physics.OverlapSphere(pos, 0.02f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
            {
                if (c == null) continue;
                var n = c.gameObject.name;
                if (n == "Player" || n.StartsWith("Remote", StringComparison.Ordinal)) continue;
                if (c.GetComponentInParent<HawkesNetAvatars>() != null && c is CapsuleCollider) continue;
                // Point-in-bounds is enough for box/mesh colliders used by arena props.
                if (c.bounds.Contains(pos))
                {
                    name = n;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Orbit a dedicated audit camera (never the gameplay follow cam) around <paramref name="center"/> for
        /// <see cref="OrbitSeconds"/> and count frames whose lens sits inside a static collider.
        /// </summary>
        public static IEnumerator Orbit(Vector3 center, float radius, float height, float nearClip, Report report, Action onPublish = null)
        {
            if (report == null) yield break;
            var go = new GameObject("HawkesNetSceneAuditCam");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; // probe only: do not steal the player's view or cause a snap
            cam.nearClipPlane = Mathf.Max(nearClip, MinNearClip);

            report.OrbitFrames = 0;
            report.CameraInsideFrames = 0;
            report.CameraInside.Clear();
            float elapsed = 0f;
            try
            {
                while (elapsed < OrbitSeconds)
                {
                    float u = elapsed / OrbitSeconds;
                    float ang = u * Mathf.PI * 2f;
                    var pos = center + new Vector3(Mathf.Cos(ang) * radius, height, Mathf.Sin(ang) * radius);
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.LookRotation((center - pos).normalized, Vector3.up);
                    report.OrbitFrames++;
                    if (CameraInsideStatic(pos, out var who))
                    {
                        report.CameraInsideFrames++;
                        if (!string.IsNullOrEmpty(who) && !report.CameraInside.Contains(who))
                            report.CameraInside.Add(who);
                    }
                    onPublish?.Invoke();
                    elapsed += Time.deltaTime > 1e-5f ? Time.deltaTime : 1f / 60f;
                    yield return null;
                }
            }
            finally
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            report.Done = true;
            onPublish?.Invoke();
        }

        /// <summary>EditMode / unit helper: step an orbit without waiting on the player loop (no GameObject needed).</summary>
        public static void OrbitSteps(Vector3 center, float radius, float height, int frames, Report report)
        {
            if (report == null || frames < 1) return;
            for (int i = 0; i < frames; i++)
            {
                float u = frames == 1 ? 0f : i / (float)(frames - 1);
                float ang = u * Mathf.PI * 2f;
                var pos = center + new Vector3(Mathf.Cos(ang) * radius, height, Mathf.Sin(ang) * radius);
                report.OrbitFrames++;
                if (CameraInsideStatic(pos, out var who))
                {
                    report.CameraInsideFrames++;
                    if (!string.IsNullOrEmpty(who) && !report.CameraInside.Contains(who))
                        report.CameraInside.Add(who);
                }
            }
            report.Done = true;
        }

        /// <summary>JSON for <c>window.__hawkesNet.scene</c>.</summary>
        public static string ToJson(Report r)
        {
            if (r == null) return "{}";
            var b = new StringBuilder(256);
            b.Append("{\"done\":").Append(r.Done ? "true" : "false");
            b.Append(",\"staticDone\":").Append(r.StaticDone ? "true" : "false");
            b.Append(",\"nearClip\":").Append(r.NearClip.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            b.Append(",\"nearClipOk\":").Append(r.NearClipOk ? "true" : "false");
            b.Append(",\"orbitFrames\":").Append(r.OrbitFrames);
            b.Append(",\"cameraInsideFrames\":").Append(r.CameraInsideFrames);
            AppendList(b, "floating", r.Floating);
            AppendList(b, "sunk", r.Sunk);
            AppendList(b, "zfight", r.ZFight);
            AppendList(b, "insideOut", r.InsideOut);
            AppendList(b, "cameraInside", r.CameraInside);
            b.Append('}');
            return b.ToString();
        }

        static void AppendList(StringBuilder b, string key, List<string> items)
        {
            b.Append(",\"").Append(key).Append("\":[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append('"').Append((items[i] ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            b.Append(']');
        }

        /// <summary>Raise or lower a prop so its renderer bounds' bottom sits on the ground under its centre.</summary>
        public static void SnapBoundsToGround(Transform prop)
        {
            if (prop == null) return;
            var r = prop.GetComponent<Renderer>();
            if (r == null) return;
            Physics.SyncTransforms();
            var b = r.bounds;
            var origin = new Vector3(b.center.x, b.max.y + 0.5f, b.center.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, c) => a.distance.CompareTo(c.distance));
            foreach (var h in hits)
            {
                if (h.collider != null && (h.collider.transform == prop || h.collider.transform.IsChildOf(prop))) continue;
                float dy = h.point.y - b.min.y;
                prop.position += new Vector3(0f, dy, 0f);
                return;
            }
            // No hit: sit on y=0.
            prop.position += new Vector3(0f, -b.min.y, 0f);
        }
    }
}
