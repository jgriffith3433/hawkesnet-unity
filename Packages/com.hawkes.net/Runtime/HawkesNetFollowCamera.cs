using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Damped third-person follow for the Avatars sample: SphereCast pull-in at walls with ease-out when clear,
    /// yaw/pitch damping capped at <see cref="MaxTurnDegPerFrame"/> so no frame snaps, idle recentre behind the
    /// avatar after <see cref="IdleSeconds"/> still, and mouse / one-finger orbit. Publishes a camera log to the
    /// page via <see cref="HawkesNet.ShowedCamera"/> and feeds follow-cam inside counts into
    /// <see cref="HawkesNetSceneAudit"/>.
    /// </summary>
    public sealed class HawkesNetFollowCamera : MonoBehaviour
    {
        public const float MaxTurnDegPerFrame = 30f;
        public const float IdleSeconds = 2f;
        public const float IdleBehindToleranceDeg = 5f;

        [Tooltip("The avatar to follow (local player root).")]
        public Transform Target;
        [Tooltip("The gameplay camera (unparented from the avatar).")]
        public Camera Cam;
        [Tooltip("While the Join form is up, orbit input is ignored.")]
        public HawkesNetStartScreen StartScreen;
        [Tooltip("Left-side stick width (0–1 of screen): fingers here steer, not orbit.")]
        public float StickZone = 0.55f;

        public float LookHeight = 1.5f;
        public float Distance = 9f;
        public float DefaultPitch = 20f;
        public float MinPitch = -5f;
        public float MaxPitch = 70f;
        public float SphereRadius = 0.35f;
        public float PullInRate = 16f;
        public float EaseOutRate = 2.2f;
        public float YawDamp = 14f;
        public float PitchDamp = 14f;
        public float IdleRecentreRate = 3.5f;
        public float OrbitSensitivity = 0.18f;
        public float MinDistance = 1.2f;

        float wantYaw, wantPitch;
        float shownYaw, shownPitch;
        float currentDist;
        float stillFor;
        float lastPublishedYaw, lastPublishedPitch;
        float frameYawDelta, framePitchDelta;
        float maxYawDelta, maxPitchDelta;
        int insideFrames;
        int frames;
        bool idleBehindOk;
        bool touchOrbitActive;
        int orbitFinger = -1;
        Vector2 orbitLast;
        bool motionThisFrame;
        readonly StringBuilder buf = new StringBuilder(384);
        string lastJson = "";

        /// <summary>Yaw the camera is showing (degrees, Unity Y).</summary>
        public float Yaw => shownYaw;
        /// <summary>Pitch the camera is showing (degrees, positive looks down onto the avatar).</summary>
        public float Pitch => shownPitch;
        public int InsideFrames => insideFrames;
        public float MaxYawDelta => maxYawDelta;
        public float MaxPitchDelta => maxPitchDelta;
        public bool IdleBehindOk => idleBehindOk;

        /// <summary>Wire an unparented main camera behind <paramref name="target"/> and return the follow component.</summary>
        public static HawkesNetFollowCamera Attach(Camera cam, Transform target, HawkesNetStartScreen start = null)
        {
            if (cam == null) throw new ArgumentNullException(nameof(cam));
            if (target == null) throw new ArgumentNullException(nameof(target));
            cam.transform.SetParent(null, true);
            var follow = cam.GetComponent<HawkesNetFollowCamera>();
            if (follow == null) follow = cam.gameObject.AddComponent<HawkesNetFollowCamera>();
            follow.Cam = cam;
            follow.Target = target;
            follow.StartScreen = start;
            if (cam.nearClipPlane < HawkesNetSceneAudit.MinNearClip)
                cam.nearClipPlane = HawkesNetSceneAudit.MinNearClip;
            follow.ResetBehindTarget();
            follow.ApplyPose(instant: true, dt: 1f / 60f);
            return follow;
        }

        public void ResetBehindTarget()
        {
            float behind = Target != null ? Target.eulerAngles.y : 0f;
            wantYaw = shownYaw = behind;
            wantPitch = shownPitch = DefaultPitch;
            currentDist = Distance;
            stillFor = 0f;
            idleBehindOk = false;
            lastPublishedYaw = shownYaw;
            lastPublishedPitch = shownPitch;
            frameYawDelta = framePitchDelta = 0f;
            maxYawDelta = maxPitchDelta = 0f;
        }

        /// <summary>Sample calls this when the avatar walked or the player turned it this frame.</summary>
        public void NotifyMotion(bool moving)
        {
            motionThisFrame = moving;
            if (moving) stillFor = 0f;
        }

        /// <summary>EditMode / tests: set the wanted yaw directly (degrees).</summary>
        public void SetWantedYaw(float yawDegrees) => wantYaw = Mathf.Repeat(yawDegrees + 360f, 360f);

        /// <summary>EditMode / tests: pretend the avatar has been still this long.</summary>
        public void SetStillFor(float seconds) => stillFor = Mathf.Max(0f, seconds);

        void LateUpdate() => Tick(Time.deltaTime, Time.unscaledDeltaTime, readInput: true);

        /// <summary>One camera step (play mode or EditMode tests). <paramref name="dt"/> drives damping and pull-in.</summary>
        public void Tick(float dt, float unscaledDt, bool readInput = false)
        {
            if (Cam == null) Cam = Camera.main;
            if (Cam == null || Target == null) return;
            dt = Mathf.Max(1e-4f, dt);
            unscaledDt = Mathf.Max(1e-4f, unscaledDt);

            bool blocked = StartScreen != null && StartScreen.Blocking;
            bool moving = motionThisFrame;
            motionThisFrame = false;
            bool orbiting = false;
            if (readInput && !blocked)
            {
                orbiting = ReadOrbitInput();
                if (orbiting) stillFor = 0f;
            }

            if (!blocked && !orbiting && !moving)
            {
                stillFor += unscaledDt;
                if (stillFor >= IdleSeconds)
                {
                    float behind = Target.eulerAngles.y;
                    wantYaw = Mathf.MoveTowardsAngle(wantYaw, behind, IdleRecentreRate * 60f * dt);
                }
            }
            else if (moving) stillFor = 0f;

            // Damp toward wanted angles, then hard-cap the shown turn so no frame exceeds MaxTurnDegPerFrame.
            float yawStep = Mathf.DeltaAngle(shownYaw, wantYaw);
            float pitchStep = wantPitch - shownPitch;
            float yawDampMax = Mathf.Abs(yawStep) * (1f - Mathf.Exp(-YawDamp * dt));
            float pitchDampMax = Mathf.Abs(pitchStep) * (1f - Mathf.Exp(-PitchDamp * dt));
            float yawMove = Mathf.Sign(yawStep) * Mathf.Min(yawDampMax, MaxTurnDegPerFrame);
            float pitchMove = Mathf.Sign(pitchStep) * Mathf.Min(pitchDampMax, MaxTurnDegPerFrame);
            if (Mathf.Abs(yawMove) > MaxTurnDegPerFrame) yawMove = Mathf.Sign(yawMove) * MaxTurnDegPerFrame;
            if (Mathf.Abs(pitchMove) > MaxTurnDegPerFrame) pitchMove = Mathf.Sign(pitchMove) * MaxTurnDegPerFrame;
            shownYaw = Mathf.Repeat(shownYaw + yawMove + 360f, 360f);
            shownPitch = Mathf.Clamp(shownPitch + pitchMove, MinPitch, MaxPitch);

            frameYawDelta = Mathf.Abs(Mathf.DeltaAngle(lastPublishedYaw, shownYaw));
            framePitchDelta = Mathf.Abs(shownPitch - lastPublishedPitch);
            if (frames > 0)
            {
                if (frameYawDelta > maxYawDelta) maxYawDelta = frameYawDelta;
                if (framePitchDelta > maxPitchDelta) maxPitchDelta = framePitchDelta;
            }
            lastPublishedYaw = shownYaw;
            lastPublishedPitch = shownPitch;

            ApplyPose(instant: false, dt);

            float behindErr = Mathf.Abs(Mathf.DeltaAngle(shownYaw, Target.eulerAngles.y));
            if (stillFor >= IdleSeconds && behindErr <= IdleBehindToleranceDeg) idleBehindOk = true;

            if (HawkesNetSceneAudit.CameraInsideStatic(Cam.transform.position, out var who))
            {
                insideFrames++;
                HawkesNetSceneAudit.NoteFollowInside(who);
            }
            else HawkesNetSceneAudit.NoteFollowClear();

            frames++;
            Publish();
        }

        void ApplyPose(bool instant, float dt)
        {
            var pivot = Target.position + Vector3.up * LookHeight;
            var rot = Quaternion.Euler(shownPitch, shownYaw, 0f);
            var ideal = pivot + rot * new Vector3(0f, 0f, -Distance);
            float wanted = HitDistance(pivot, ideal);

            if (instant) currentDist = wanted;
            else
            {
                float rate = wanted < currentDist - 1e-4f ? PullInRate : EaseOutRate;
                currentDist = Mathf.MoveTowards(currentDist, wanted, rate * Mathf.Max(1e-4f, dt) * Distance);
            }
            currentDist = Mathf.Clamp(currentDist, MinDistance, Distance);

            var pos = pivot + rot * new Vector3(0f, 0f, -currentDist);
            // Hard keep-out: if a hitch still leaves the eye inside, pull in along the ray until clear.
            if (HawkesNetSceneAudit.CameraInsideStatic(pos, out _))
            {
                for (int i = 0; i < 8 && HawkesNetSceneAudit.CameraInsideStatic(pos, out _); i++)
                {
                    currentDist = Mathf.Max(MinDistance, currentDist - SphereRadius);
                    pos = pivot + rot * new Vector3(0f, 0f, -currentDist);
                }
            }

            Cam.transform.position = pos;
            Cam.transform.rotation = Quaternion.LookRotation((pivot - pos).normalized, Vector3.up);
        }

        float HitDistance(Vector3 pivot, Vector3 ideal)
        {
            var delta = ideal - pivot;
            float full = delta.magnitude;
            if (full < 1e-4f) return MinDistance;
            var dir = delta / full;
            var hits = Physics.SphereCastAll(pivot, SphereRadius, dir, full, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (!IsObstacle(h.collider)) continue;
                return Mathf.Max(MinDistance, h.distance);
            }
            return full;
        }

        static bool IsObstacle(Collider c)
        {
            if (c == null) return false;
            var n = c.gameObject.name;
            if (n == "Ground" || n == "Player" || n.StartsWith("Remote", StringComparison.Ordinal)) return false;
            if (c.GetComponentInParent<HawkesNetNamePlate>() != null) return false;
            if (c is CapsuleCollider && c.GetComponentInParent<HawkesNetAvatars>() != null) return false;
            for (var t = c.transform; t != null; t = t.parent)
                if (t.name == "Humanoid" || t.name == "Player" || t.name.StartsWith("Remote", StringComparison.Ordinal)) return false;
            return true;
        }

        bool ReadOrbitInput()
        {
            bool any = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                // Mouse: right-drag orbits (left is free for UI / click Join).
                if (Input.GetMouseButton(1))
                {
                    float dx = Input.GetAxisRaw("Mouse X");
                    float dy = Input.GetAxisRaw("Mouse Y");
                    if (Mathf.Abs(dx) > 1e-5f || Mathf.Abs(dy) > 1e-5f)
                    {
                        AddOrbit(dx * OrbitSensitivity * 40f, -dy * OrbitSensitivity * 40f);
                        any = true;
                        touchOrbitActive = false;
                    }
                }

                int n = Input.touchCount;
                for (int i = 0; i < n; i++)
                {
                    Touch t = Input.GetTouch(i);
                    // Right half of the screen orbits; left half is the move stick.
                    bool onOrbitSide = t.position.x >= Screen.width * StickZone;
                    if (t.phase == TouchPhase.Began && onOrbitSide && orbitFinger < 0 && !OnEmoteButton(t.position))
                    {
                        orbitFinger = t.fingerId;
                        orbitLast = t.position;
                        touchOrbitActive = true;
                    }
                    if (t.fingerId != orbitFinger) continue;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    {
                        orbitFinger = -1;
                        continue;
                    }
                    Vector2 d = t.position - orbitLast;
                    orbitLast = t.position;
                    if (d.sqrMagnitude > 0.01f)
                    {
                        AddOrbit(d.x * OrbitSensitivity, -d.y * OrbitSensitivity);
                        any = true;
                        touchOrbitActive = true;
                    }
                }
                if (orbitFinger >= 0)
                {
                    bool seen = false;
                    for (int i = 0; i < n; i++) if (Input.GetTouch(i).fingerId == orbitFinger) { seen = true; break; }
                    if (!seen) orbitFinger = -1;
                }
            }
            catch (InvalidOperationException) { /* Input System only */ }
#endif
            return any;
        }

        static bool OnEmoteButton(Vector2 screenPos)
        {
            float w = Screen.width, h = Screen.height;
            return screenPos.x > w * 0.78f && screenPos.y < h * 0.42f;
        }

        void AddOrbit(float yawDelta, float pitchDelta)
        {
            wantYaw = Mathf.Repeat(wantYaw + yawDelta + 360f, 360f);
            wantPitch = Mathf.Clamp(wantPitch + pitchDelta, MinPitch, MaxPitch);
            stillFor = 0f;
            idleBehindOk = false;
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        void Publish()
        {
            float behindErr = Target != null ? Mathf.Abs(Mathf.DeltaAngle(shownYaw, Target.eulerAngles.y)) : 0f;
            buf.Length = 0;
            buf.Append("{\"yaw\":").Append(F(shownYaw))
                .Append(",\"pitch\":").Append(F(shownPitch))
                .Append(",\"yawDelta\":").Append(F(frameYawDelta))
                .Append(",\"pitchDelta\":").Append(F(framePitchDelta))
                .Append(",\"maxYawDelta\":").Append(F(maxYawDelta))
                .Append(",\"maxPitchDelta\":").Append(F(maxPitchDelta))
                .Append(",\"insideFrames\":").Append(insideFrames)
                .Append(",\"followInsideFrames\":").Append(HawkesNetSceneAudit.FollowInsideFrames)
                .Append(",\"distance\":").Append(F(currentDist))
                .Append(",\"wantedDistance\":").Append(F(Distance))
                .Append(",\"stillSeconds\":").Append(F(stillFor))
                .Append(",\"idleRecentering\":").Append(stillFor >= IdleSeconds ? "true" : "false")
                .Append(",\"behindErrorDeg\":").Append(F(behindErr))
                .Append(",\"idleBehindOk\":").Append(idleBehindOk ? "true" : "false")
                .Append(",\"touchOrbit\":").Append(touchOrbitActive ? "true" : "false")
                .Append(",\"frames\":").Append(frames)
                .Append('}');
            string json = buf.ToString();
            if (json == lastJson) return;
            lastJson = json;
            HawkesNet.ShowedCamera(json);
        }
    }
}
