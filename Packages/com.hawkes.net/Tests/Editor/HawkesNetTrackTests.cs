#if UNITY_INCLUDE_TESTS
// Compiled only where Unity's Test Framework is on and this package is in the project's testables; elsewhere
// (the demo's cloud build has no com.unity.test-framework) this file is empty, so NUnit is never needed.
using NUnit.Framework;

namespace Hawkes.Net.Tests
{
    /// <summary>Edit-mode tests (Window > General > Test Runner > EditMode) for HawkesNetTrack's extrapolation clamp.</summary>
    public class HawkesNetTrackTests
    {
        [Test]
        public void ExtrapolationNeverPassesTheLimit()
        {
            double wall = 0;
            var t = new HawkesNetTrack(() => wall);
            double at = 0;
            for (int i = 0; i <= 30; i++) { at = i * 33.0; t.Push(at, (float)(at * 0.005), 1f, 0f, 90f, at + 40); wall = at; }
            t.MaxAhead = 10000; // asking for more than the limit changes nothing
            double maxAhead = 0;
            for (double now = at; now < at + 4000; now += 16)
            {
                wall += 16;
                Assert.IsTrue(t.Sample(now, out _, out _, out _, out _));
                maxAhead = System.Math.Max(maxAhead, t.Ahead);
                Assert.LessOrEqual(t.Ahead, HawkesNetTrack.AheadLimit + 1e-6, "Ahead at room time " + now);
            }
            Assert.AreEqual(HawkesNetTrack.AheadLimit, maxAhead, 1e-6, "it was carried on right up to the limit");
        }

        [Test]
        public void AfterTheLimitThePlayerHoldsStill()
        {
            double wall = 0;
            var t = new HawkesNetTrack(() => wall);
            double at = 0;
            for (int i = 0; i <= 30; i++) { at = i * 33.0; t.Push(at, (float)(at * 0.005), 1f, 0f, 90f, at + 40); wall = at; }
            float lastX = 0;
            bool held = false;
            for (double now = at; now < at + 5000; now += 16)
            {
                wall += 16;
                t.Sample(now, out float x, out _, out _, out _);
                if (held) Assert.AreEqual(lastX, x, 1e-5f, "drifted at room time " + now);
                // Delay is small here, so give the clamp time to be reached, then it must not move again.
                if (!held && t.Ahead >= HawkesNetTrack.AheadLimit - 1e-6) held = true;
                lastX = x;
            }
            Assert.IsTrue(held, "the limit was reached");
            // At most 250 ms * 5 m/s = 1.25 m past the last pose.
            t.Sample(at + 5000, out float fx, out _, out _, out _);
            Assert.LessOrEqual(fx - at * 0.005f, 1.25f + 0.05f);
        }

        [Test]
        public void ALimitBelowTheMaximumIsKept()
        {
            double wall = 0;
            var t = new HawkesNetTrack(() => wall) { MaxAhead = 100 };
            double at = 0;
            for (int i = 0; i <= 30; i++) { at = i * 33.0; t.Push(at, (float)(at * 0.005), 1f, 0f, 90f, at + 40); wall = at; }
            for (double now = at; now < at + 2000; now += 16) { wall += 16; t.Sample(now, out _, out _, out _, out _); }
            Assert.AreEqual(100, t.Ahead, 1e-6);
        }
    }
}
#endif
