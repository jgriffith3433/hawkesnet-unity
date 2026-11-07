// HawkesNetTrack (Packages/com.hawkes.net/Runtime/HawkesNetTrack.cs) with plain dotnet: a remote player walks a figure
// eight, facing where it goes, and sends its pose the way the SDK does (the newest about 30 times a second, stamped on
// the room's clock); the poses arrive late (a base latency, then 150 ms more with jitter, in order as on a websocket,
// and a stall); the track is sampled at 60 frames a second. Checks: the avatar moves every frame, never steps faster
// than the player could move or turn (a snap), and stays near where the player really was.
//   dotnet run --project DevTools/TrackTest   (the .csproj is made by the command in README.md: *.csproj is ignored)
using System;
using System.Collections.Generic;
using Hawkes;

static class Program
{
    static int failed;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failed++; }

    // The path: a figure eight 6 m wide, about 2.4 to 3.4 m/s, facing along it (Unity yaw: 0 is +z, 90 is +x).
    const double W = 0.8, A = 3;
    static void Truth(double ms, out double x, out double z, out double yaw)
    {
        double t = ms / 1000 * W;
        x = A * Math.Sin(t); z = A * Math.Sin(t) * Math.Cos(t);
        double dx = A * Math.Cos(t), dz = A * Math.Cos(2 * t);
        yaw = Math.Atan2(dx, dz) * 180 / Math.PI;
    }
    const double MaxSpeed = A * W * 1.4143;
    // The path's top turn rate (degrees a second).
    static readonly double MaxTurn = TopTurn();
    static double TopTurn()
    {
        double top = 0;
        for (double ms = 0; ms < 8000; ms += 5)
        {
            Truth(ms, out _, out _, out var a); Truth(ms + 5, out _, out _, out var b);
            top = Math.Max(top, Math.Abs(((b - a) % 360 + 540) % 360 - 180) / 0.005);
        }
        return top;
    }

    struct Run { public string name; public double lagFrom, lagTo, switchAt, jitter, stallAt, stallMs, frameJitter, clockJumpAt, clockJump; public bool stop, old; }

    // How HawkesNetAvatars played poses before 0.8.0 (for comparison only): 120 ms behind, held at the newest.
    sealed class OldPlayback
    {
        readonly List<(double at, float x, float z, float yaw)> b = new List<(double, float, float, float)>();
        public void Push(double at, float x, float z, float yaw) { b.Add((at, x, z, yaw)); if (b.Count > 32) b.RemoveAt(0); }
        public void Sample(double now, out float x, out float z, out float yaw)
        {
            double at = now - 120;
            while (b.Count > 2 && b[1].at <= at) b.RemoveAt(0);
            var a = b[0]; var c = b.Count > 1 ? b[1] : b[0];
            float t = c.at > a.at ? (float)Math.Max(0, Math.Min(1, (at - a.at) / (c.at - a.at))) : 1f;
            if (b.Count == 1 || at >= c.at) { a = c; t = 1f; }
            x = a.x + (c.x - a.x) * t; z = a.z + (c.z - a.z) * t;
            float d = ((c.yaw - a.yaw) % 360 + 540) % 360 - 180; yaw = a.yaw + d * t;
        }
    }

    static void Play(Run r)
    {
        var rng = new Random(7);
        double wallNow = 0;
        var track = new HawkesNetTrack(() => wallNow);
        var old = new OldPlayback();
        var flight = new List<(double arrive, double at, double x, double z, double yaw)>();
        double lastArrive = 0, nextFlush = 0, frame = 0;
        double px = 0, pz = 0, pyaw = 0, maxStep = 0, maxTurn = 0, sumErr = 0, maxErr = 0;
        int frames = 0, still = 0, snaps = 0, turnSnaps = 0, measured = 0;
        bool have = false;
        double end = 16000, from = 2000;
        double stopAt = r.stop ? 9000 : double.MaxValue, stopFor = 1500;
        double Sender(double ms) => ms < stopAt ? ms : ms < stopAt + stopFor ? stopAt : ms - stopFor; // where on the path, after a stop
        for (double t = 0; t < end; t += 1)
        {
            wallNow = t;
            // The sender: a frame each 16.7 ms sets the newest pose; every 33 ms the newest goes out.
            if (t >= nextFlush)
            {
                nextFlush += 33.3;
                double stamp = Math.Floor(t / 16.67) * 16.67;
                Truth(Sender(stamp), out var x, out var z, out var yaw);
                double lag = 40 + (t < r.switchAt ? r.lagFrom : r.lagTo) + rng.NextDouble() * r.jitter;
                if (t >= r.stallAt && t < r.stallAt + r.stallMs) lag += r.stallAt + r.stallMs - t; // nothing gets through, then a burst
                double arrive = Math.Max(lastArrive, t + lag);
                lastArrive = arrive;
                bool resting = Sender(stamp) == stopAt && stamp > stopAt + 100;
                if (!resting) flight.Add((arrive, stamp, x, z, yaw)); // the SDK sends nothing while nothing changes
            }
            // The receiver's frame: the poses that arrived (the newest per frame, as the bridge gives them), then a sample.
            if (t >= frame)
            {
                frame += 16.67 + (rng.NextDouble() - 0.5) * r.frameJitter;
                int newest = -1;
                for (int i = 0; i < flight.Count; i++) if (flight[i].arrive <= t) newest = i;
                if (newest >= 0)
                {
                    var p = flight[newest];
                    track.Push(p.at, (float)p.x, 1f, (float)p.z, (float)p.yaw, t + (t >= r.clockJumpAt ? r.clockJump : 0));
                    old.Push(p.at, (float)p.x, (float)p.z, (float)p.yaw);
                    flight.RemoveRange(0, newest + 1);
                }
                double roomNow = t + (t >= r.clockJumpAt ? r.clockJump : 0); // the receiver's room clock (a correction of its offset)
                if (!track.Sample(roomNow, out float x, out float y, out float z, out float yaw)) continue;
                if (r.old) old.Sample(t, out x, out z, out yaw);
                if (have && t >= from)
                {
                    double dt = 16.67;
                    double step = Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                    double turn = Math.Abs(((yaw - pyaw) % 360 + 540) % 360 - 180);
                    double shownAt = roomNow - track.Delay; // the moment drawn, on the stamps' clock
                    frames++;
                    bool restingShown = r.stop && shownAt > stopAt - 50 && shownAt < stopAt + stopFor + 50;
                    if (step < 1e-4 && !restingShown) still++;
                    if (step > Math.Max(0.05, MaxSpeed * 2 * dt / 1000)) snaps++;
                    if (turn > Math.Max(4, MaxTurn * 2 * dt / 1000)) turnSnaps++; // as the playtest bot's "smooth" counts them
                    maxStep = Math.Max(maxStep, step / dt * 1000);
                    maxTurn = Math.Max(maxTurn, turn / dt * 1000);
                    if (!restingShown)
                    {
                        Truth(Sender(shownAt), out var tx, out var tz, out _);
                        double err = Math.Sqrt((x - tx) * (x - tx) + (z - tz) * (z - tz));
                        sumErr += err; maxErr = Math.Max(maxErr, err); measured++;
                    }
                }
                px = x; pz = z; pyaw = yaw; have = true;
            }
        }
        if (r.old) { Console.WriteLine($"  before 0.8.0, {r.name}: {frames} frames, still {still}, snaps {snaps} (turn {turnSnaps}), top speed {maxStep:0.0} m/s, error max {maxErr:0.00} m"); return; }
        Console.WriteLine($"  {r.name}: {frames} frames, still {still}, snaps {snaps} (turn {turnSnaps}), top speed {maxStep:0.0} m/s (path {MaxSpeed:0.0}), top turn {maxTurn:0} deg/s (path {MaxTurn:0}), delay {track.Delay:0} ms, transit {track.Transit:0} jitter {track.Jitter:0}, extrapolated {track.Extrapolated}, error mean {sumErr / Math.Max(1, measured):0.000} max {maxErr:0.000} m");
        Check(still == 0, $"{r.name}: the avatar moves every frame");
        Check(snaps == 0 && turnSnaps == 0, $"{r.name}: no snapping (no step faster than twice the path's top speed, no turn faster than twice its top turn rate)");
        Check(maxErr < 0.35, $"{r.name}: drawn within 0.35 m of where the player was (max {maxErr:0.00} m)");
    }

    // The clamp (also the package's edit-mode test, Tests/Editor): a player that stops sending is carried on at most 250 ms, then holds.
    static void Clamp()
    {
        double wall = 0, at = 0;
        var t = new HawkesNetTrack(() => wall) { MaxAhead = 10000 };
        for (int i = 0; i <= 30; i++) { at = i * 33.0; t.Push(at, (float)(at * 0.005), 1f, 0f, 90f, at + 40); wall = at; }
        double maxAhead = 0, minHeldX = double.MaxValue, maxHeldX = double.MinValue;
        for (double now = at; now < at + 5000; now += 16)
        {
            wall += 16;
            t.Sample(now, out float x, out _, out _, out _);
            maxAhead = Math.Max(maxAhead, t.Ahead);
            if (now > at + 1500) { minHeldX = Math.Min(minHeldX, x); maxHeldX = Math.Max(maxHeldX, x); }
        }
        Check(maxAhead <= HawkesNetTrack.AheadLimit + 1e-6 && maxAhead > HawkesNetTrack.AheadLimit - 1, $"clamp: carried on up to {maxAhead:0} ms, never past {HawkesNetTrack.AheadLimit:0}");
        Check(maxHeldX - minHeldX < 1e-4, $"clamp: holds still after the limit (moved {maxHeldX - minHeldX:0.00000} m)");
    }

    static int Main()
    {
        Clamp();
        Play(new Run { name = "clean", lagFrom = 0, lagTo = 0, switchAt = 1e9, jitter = 8, stallAt = 1e9, frameJitter = 2, clockJumpAt = 1e9 });
        Play(new Run { name = "150 ms + 30 ms jitter", lagFrom = 150, lagTo = 150, switchAt = 1e9, jitter = 30, stallAt = 1e9, frameJitter = 2, clockJumpAt = 1e9 });
        Play(new Run { name = "0 -> 150 ms mid-run", lagFrom = 0, lagTo = 150, switchAt = 7000, jitter = 30, stallAt = 1e9, frameJitter = 2, clockJumpAt = 1e9 });
        Play(new Run { name = "150 ms, a 200 ms stall", lagFrom = 150, lagTo = 150, switchAt = 1e9, jitter = 30, stallAt = 8000, stallMs = 200, frameJitter = 2, clockJumpAt = 1e9 });
        Play(new Run { name = "150 ms, the room's clock corrected by 80 ms", lagFrom = 150, lagTo = 150, switchAt = 1e9, jitter = 30, stallAt = 1e9, frameJitter = 2, clockJumpAt = 8000, clockJump = 80 });
        Play(new Run { name = "150 ms, stops and goes", lagFrom = 150, lagTo = 150, switchAt = 1e9, jitter = 30, stallAt = 1e9, frameJitter = 2, clockJumpAt = 1e9, stop = true });
        Play(new Run { name = "clean", lagFrom = 0, lagTo = 0, switchAt = 1e9, jitter = 8, stallAt = 1e9, frameJitter = 2, clockJumpAt = 1e9, old = true });
        Play(new Run { name = "150 ms + 30 ms jitter", lagFrom = 150, lagTo = 150, switchAt = 1e9, jitter = 30, stallAt = 1e9, frameJitter = 2, clockJumpAt = 1e9, old = true });
        Console.WriteLine(failed == 0 ? "all passed" : $"{failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
