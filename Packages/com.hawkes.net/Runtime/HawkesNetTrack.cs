using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Hawkes
{
    /// <summary>
    /// One remote player's poses, played back smoothly a little in the past (the web SDK's Track, the same rules, so a
    /// Unity page and a web page draw a player the same way). Push each pose as it arrives (its stamp is the room's
    /// clock); Sample once a frame with the room's clock:
    /// - interpolated: drawn between the two poses around (now - Delay): position lerped, facing turned the short way;
    /// - Delay learns itself: how late this player's poses arrive (transit), how often they come (interval) and how
    ///   much that varies (jitter), and eases towards the new value (playback runs at most 10% fast or slow), so a
    ///   slow or jittery connection costs a little more delay instead of stutter;
    /// - extrapolated: when a pose is late, the player is carried on along its velocity and turn (up to MaxAhead ms),
    ///   and when the real pose arrives the guess's error is blended out over about a tenth of a second (no snap);
    /// - a player that stood still and starts again starts from where it stood (no slow slide from the last pose).
    /// HawkesNetAvatars uses one per remote; a game with its own avatars (HawkesNetLobby.PoseChanged) can too.
    /// No UnityEngine in its core (Push/Sample with numbers), so it is tested with plain dotnet (DevTools/TrackTest).
    /// </summary>
    public sealed class HawkesNetTrack
    {
        struct Snap { public double at; public float x, y, z, yaw; }

        readonly List<Snap> buf = new List<Snap>();
        readonly List<double> gaps = new List<double>();
        readonly double[] sorted = new double[9];
        readonly Func<double> wall;
        double transit, jitter = 8, delay = -1, played, sampledAt = double.NegativeInfinity, errAt;
        bool linked;
        // The last frame drawn (after the blend), its velocity, and the correction still being blended out.
        float ox, oy, oz, oyaw, vx, vy, vz, vyaw, ex, ey, ez, eyaw;
        bool drawn, guessed;
        double newestAt = double.NaN;

        /// <summary>wall: a clock that only runs forward (ms), for easing the delay and the blend; default a stopwatch.</summary>
        public HawkesNetTrack(Func<double> wall = null)
        {
            if (wall == null) { var sw = System.Diagnostics.Stopwatch.StartNew(); wall = () => sw.Elapsed.TotalMilliseconds; }
            this.wall = wall;
        }

        /// <summary>The least delay (ms); the learned one is used when it is more.</summary>
        public double MinDelay;
        /// <summary>How far past the newest pose a late player is carried on (ms).</summary>
        public double MaxAhead = 250;
        /// <summary>The median time between this player's poses (ms).</summary>
        public double Interval { get; private set; } = 100;
        /// <summary>How late this player's poses arrive, the usual (ms; the room's clock, so it includes the network).</summary>
        public double Transit => linked ? transit : 0;
        /// <summary>How much later than usual they arrive, on average (ms).</summary>
        public double Jitter => jitter;
        /// <summary>How far behind the room's clock this player is drawn now (ms; eases towards Target).</summary>
        public double Delay => delay < 0 ? Target : delay;
        /// <summary>How far past its newest pose this player was carried on in the last Sample (ms; 0 while interpolating).</summary>
        public double Ahead { get; private set; }
        /// <summary>Poses held.</summary>
        public int Count => buf.Count;
        /// <summary>Frames drawn between two poses, and frames carried on past the newest (since the track was made).</summary>
        public long Interpolated { get; private set; }
        public long Extrapolated { get; private set; }

        /// <summary>The delay it eases towards: transit, one interval and room for the jitter.</summary>
        public double Target => Math.Max(MinDelay, Math.Max(0, Transit) + Math.Min(Interval, 1500) * 1.05 + jitter * 2.5 + 8);

        /// <summary>A pose: its stamp (the room's clock, ms), position and yaw (degrees). now: the room's clock as it
        /// arrived (for how late it is). A pose older than the newest is dropped (late, out of order).</summary>
        public void Push(double at, float x, float y, float z, float yaw, double now)
        {
            if (double.IsNaN(at) || double.IsInfinity(at)) at = now;
            var s = new Snap { at = at, x = x, y = y, z = z, yaw = yaw };
            if (buf.Count > 0)
            {
                var last = buf[buf.Count - 1];
                if (at <= last.at) { if (at == last.at) buf[buf.Count - 1] = s; return; }
                double gap = at - last.at;
                gaps.Add(gap);
                if (gaps.Count > 9) gaps.RemoveAt(0);
                gaps.CopyTo(sorted);
                Array.Sort(sorted, 0, gaps.Count);
                Interval = Math.Max(16, Math.Min(3000, sorted[gaps.Count / 2]));
                // It rested (nothing was sent while it stood still): it stayed where it was until just before it moved.
                if (gap > Interval * 2.5 && Still(buf.Count - 1)) buf.Add(new Snap { at = at - Math.Min(gap / 2, Interval), x = last.x, y = last.y, z = last.z, yaw = last.yaw });
            }
            buf.Add(s);
            while (buf.Count > 3 && buf[1].at < at - 3000) buf.RemoveAt(0);
            while (buf.Count > 48) buf.RemoveAt(0);
            // How late it came: the usual (follows the quick ones fast, the slow ones slowly) and the jitter above it.
            double d = now - at;
            if (double.IsNaN(d) || double.IsInfinity(d)) return;
            if (!linked) { transit = d; linked = true; }
            else if (d < transit) transit += (d - transit) * 0.3;
            else transit += (d - transit) * 0.01;
            jitter += (Math.Max(0, d - transit) - jitter) * 0.06;
        }

        bool Still(int i)
        {
            if (i <= 0) return true;
            Snap s = buf[i], p = buf[i - 1];
            double moved = Math.Abs(s.x - p.x) + Math.Abs(s.y - p.y) + Math.Abs(s.z - p.z);
            return moved / Math.Max(1, s.at - p.at) * 1000 < 0.1;
        }

        /// <summary>Where to draw the player at the room's time now (ms): false until the first pose.</summary>
        public bool Sample(double now, out float x, out float y, out float z, out float yaw)
        {
            x = y = z = yaw = 0;
            if (buf.Count == 0) return false;
            double w = wall(), since = w - sampledAt;
            // The moment drawn runs with the wall clock and eases towards (now - Target) at most 10% fast or slow, so a
            // change in the delay, or in the room's clock (a correction of its offset), never shows as a jump.
            double want = now - Target;
            if (delay < 0 || since > 1000 || Math.Abs(want - (played + since)) > 1000) played = want;
            else { played += since; double room = since * 0.1; played += Math.Max(-room, Math.Min(room, want - played)); }
            delay = now - played;
            sampledAt = w;
            bool wasGuess = guessed;
            Read(played, out x, out y, out z, out yaw);
            double newest = buf[buf.Count - 1].at;
            // A guess that newer poses replaced: whatever this frame would have drawn had it gone on is the start, and
            // the difference to where the player really was is blended out (90 ms), so the correction never shows as a jump.
            if (drawn && wasGuess && newest != newestAt)
            {
                double dt = Math.Min(since, 100) / 1000.0;
                float px = ox + vx * (float)dt, py = oy + vy * (float)dt, pz = oz + vz * (float)dt, pyaw = oyaw + vyaw * (float)dt;
                float k0 = Blend(w);
                float dx = px - (x + ex * k0), dy = py - (y + ey * k0), dz = pz - (z + ez * k0), dyaw = Angle(pyaw - (yaw + eyaw * k0));
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 10)
                {
                    ex = ex * k0 + dx; ey = ey * k0 + dy; ez = ez * k0 + dz; eyaw = Angle(eyaw * k0 + dyaw);
                    errAt = w;
                }
                else { ex = ey = ez = eyaw = 0; } // a jump that big is a teleport: show it
            }
            newestAt = newest;
            float k = Blend(w);
            if (k > 0) { x += ex * k; y += ey * k; z += ez * k; yaw += eyaw * k; }
            else ex = ey = ez = eyaw = 0;
            yaw = Angle(yaw);
            if (drawn && since > 0 && since < 250)
            {
                float inv = (float)(1000.0 / since);
                vx = (x - ox) * inv; vy = (y - oy) * inv; vz = (z - oz) * inv; vyaw = Angle(yaw - oyaw) * inv;
            }
            else vx = vy = vz = vyaw = 0;
            ox = x; oy = y; oz = z; oyaw = yaw; drawn = true;
            return true;
        }

        float Blend(double w)
        {
            if (ex == 0 && ey == 0 && ez == 0 && eyaw == 0) return 0;
            double k = Math.Exp(-(w - errAt) / 90.0);
            return k < 0.02 ? 0 : (float)k;
        }

        void Read(double rt, out float x, out float y, out float z, out float yaw)
        {
            int i = buf.Count - 1;
            while (i > 0 && buf[i].at > rt) i--;
            var a = buf[i];
            if (i == buf.Count - 1 || rt < a.at)
            {
                // Past the newest (or before the oldest): hold it, carried on along its last velocity and turn.
                double ahead = rt < a.at ? 0 : Math.Min(rt - a.at, MaxAhead);
                Ahead = Math.Max(0, rt - a.at);
                x = a.x; y = a.y; z = a.z; yaw = a.yaw;
                if (ahead > 0 && buf.Count > 1)
                {
                    var p = buf[buf.Count - 2];
                    double k = ahead / Math.Max(1, a.at - p.at);
                    x += (float)((a.x - p.x) * k); y += (float)((a.y - p.y) * k); z += (float)((a.z - p.z) * k);
                    // A turn is carried on for at most a quarter turn, however late.
                    yaw += (float)Math.Max(-90, Math.Min(90, Angle(a.yaw - p.yaw) * k));
                }
                guessed = Ahead > 0 && buf.Count > 1;
                if (Ahead > 0) Extrapolated++;
                return;
            }
            var c = buf[i + 1];
            float t = (float)((rt - a.at) / Math.Max(1e-3, c.at - a.at));
            Ahead = 0; guessed = false; Interpolated++;
            x = a.x + (c.x - a.x) * t; y = a.y + (c.y - a.y) * t; z = a.z + (c.z - a.z) * t;
            yaw = a.yaw + Angle(c.yaw - a.yaw) * t;
        }

        /// <summary>An angle in degrees, the short way: -180 to 180.</summary>
        static float Angle(float d)
        {
            d %= 360f;
            if (d > 180f) d -= 360f; else if (d < -180f) d += 360f;
            return d;
        }

        /// <summary>Forget everything (a teleport, a new room).</summary>
        public void Clear()
        {
            buf.Clear(); gaps.Clear(); delay = -1; linked = false; jitter = 8; Interval = 100;
            drawn = guessed = false; ex = ey = ez = eyaw = 0; newestAt = double.NaN; Ahead = 0;
        }

#if UNITY_5_3_OR_NEWER
        /// <summary>A pose as the room gives it (OnPose, HawkesNetLobby.PoseChanged); now: the room's clock (room.Now).</summary>
        public void Push(HawkesNetPose pose, double now) => Push(pose.at, pose.position.x, pose.position.y, pose.position.z, pose.yaw, now);

        /// <summary>Where to draw the player at the room's time now (room.Now): false until the first pose.</summary>
        public bool Sample(double now, out Vector3 position, out float yaw)
        {
            bool ok = Sample(now, out float x, out float y, out float z, out yaw);
            position = new Vector3(x, y, z);
            return ok;
        }
#endif
    }
}
