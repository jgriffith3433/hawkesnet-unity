#if UNITY_INCLUDE_TESTS
// Fake-transport EditMode tests for managed-client recovery (JS SDK 1.4.1+: one resume, then a fresh ticket).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Hawkes.Net.Tests
{
    public class HawkesNetReconnectTests
    {
        [Test]
        public void BlipResumesOnce()
        {
            var hub = new FakeHub { RefuseResume = false };
            var client = hub.Make(1);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");
            Assert.AreEqual(new[] { "ticket" }, hub.Kinds.ToArray());

            hub.LastWire.Drop(1001, "blip");
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "status" && (string)e["status"] == "reconnecting"), 2000), "reconnecting");
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "status" && (string)e["status"] == "online") >= 2, 2000), "back online");
            Assert.AreEqual(new[] { "ticket", "resume" }, hub.Kinds.ToArray(), "one resume after a blip");
            Assert.AreEqual(1, hub.TicketCount, "no fresh ticket on a blip");
            client.Leave();
        }

        [Test]
        public void RefusedResumeRejoinsWithinOneTry()
        {
            var hub = new FakeHub { RefuseResume = true };
            var client = hub.Make(2);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");

            hub.LastWire.Drop(1001, "node gone");
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "status" && (string)e["status"] == "online") >= 2, 2000), "back online after refused resume");
            Assert.AreEqual(new[] { "ticket", "resume", "ticket" }, hub.Kinds.ToArray(), "one refused resume, then a fresh ticket");
            Assert.AreEqual(1, hub.RefusedResumes, "at most one refused resume");
            Assert.AreEqual(2, hub.TicketCount, "fresh ticket after the refused resume");
            client.Leave();
        }

        [TestCase("410")]
        [TestCase("502")]
        [TestCase("closed")]
        public void RefusedResumeRejoinsAfterOneFreshTicketWithNoBackoff(string refusal)
        {
            var hub = new FakeHub { RefuseResume = true, Refusal = refusal };
            var client = hub.Make(3);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");
            var room = hub.LastWire;

            room.Drop(1001, "node gone");
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "status" && (string)e["status"] == "online") >= 2, 2000), "back online");
            Assert.AreNotSame(room, hub.LastWire, "a new socket");
            Assert.AreEqual(new[] { "ticket", "resume", "ticket" }, hub.Kinds.ToArray(), refusal + ": one resume, one fresh ticket");
            Assert.AreEqual(0, hub.Delays.Count, "no backoff before the fresh-ticket join");
            Assert.AreEqual(1, events.Count(e => (string)e["e"] == "status" && (string)e["status"] == "reconnecting"), "reconnecting once");
            client.Leave();
        }

        [Test]
        public void BackoffOnlyBetweenFullFailures()
        {
            var hub = new FakeHub { RefuseResume = true };
            var client = hub.Make(4);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");
            hub.Down = true;
            hub.LastWire.Drop(1001, "network");
            Assert.IsTrue(Until(() => hub.Delays.Count >= 2, 2000), "tries fail while down, with backoff between them");
            Assert.AreEqual("resume", hub.Kinds[1], "the first try after the drop is the one resume");
            Assert.AreEqual("ticket", hub.Kinds[2], "then a fresh ticket at once");
            Assert.AreEqual(500, hub.Delays[0], "the first wait comes after the first full failure");
            Assert.AreEqual(1000, hub.Delays[1]);
            hub.Down = false;
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "status" && (string)e["status"] == "online") >= 2, 2000), "joined again");
            client.Leave();
        }

        [Test]
        public void FiveSecondNetworkDropRejoinsWithinThreeSecondsOfTheReturn()
        {
            var hub = new FakeHub { RefuseResume = true, RealDelay = true };
            var client = hub.Make(5);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");
            var tickets = hub.TicketCount;

            hub.Down = hub.TicketsDown = true;
            hub.LastWire.Drop(1006, "network");
            Thread.Sleep(5000);
            Drain(client, events);
            Assert.AreEqual(1, events.Count(e => (string)e["e"] == "status" && (string)e["status"] == "reconnecting"), "reconnecting once, before the return");
            Assert.AreEqual(1, events.Count(e => (string)e["e"] == "status" && (string)e["status"] == "online"), "still not back");
            hub.Down = hub.TicketsDown = false;
            var back = DateTime.UtcNow;
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "status" && (string)e["status"] == "online") >= 2, 3000), "rejoined within 3 s of the return");
            Assert.Less((DateTime.UtcNow - back).TotalMilliseconds, 3000);
            Assert.AreEqual(1, events.Count(e => (string)e["e"] == "status" && (string)e["status"] == "reconnecting"), "OnReconnecting once");
            Assert.AreEqual(2, events.Count(e => (string)e["e"] == "status" && (string)e["status"] == "online"), "OnReconnected once");
            Assert.Greater(hub.TicketCount, tickets, "same room, a fresh ticket");
            client.Leave();
        }

        [Test]
        public void NodeRestartAsksForAFreshTicketAtOnce()
        {
            var hub = new FakeHub { RefuseResume = false };
            var client = hub.Make(6);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");

            hub.LastWire.PushFrame(new Dictionary<string, object> { ["c"] = "draining" });
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "status" && (string)e["status"] == "online") >= 2, 2000), "back online");
            Assert.AreEqual(new[] { "ticket", "ticket" }, hub.Kinds.ToArray(), "the dead resume token is never tried");
            Assert.AreEqual(0, hub.Delays.Count, "no wait");
            client.Leave();
        }

        [Test]
        public void PeerAwayAndBackEachFireOnceAcrossLeaveAndReturn()
        {
            var hub = new FakeHub();
            var client = hub.Make(7);
            var events = new List<Dictionary<string, object>>();
            client.Start();
            Assert.IsTrue(Until(() => Drain(client, events).Any(e => (string)e["e"] == "ready"), 2000), "first hello");
            var w = hub.LastWire;
            w.PushFrame(new Dictionary<string, object> { ["t"] = "join", ["c"] = "p2", ["name"] = "Pal", ["ord"] = 1.0 });
            w.PushFrame(new Dictionary<string, object> { ["t"] = "away", ["c"] = "p2", ["on"] = true });
            w.PushFrame(new Dictionary<string, object> { ["t"] = "away", ["c"] = "p2", ["on"] = false });
            Assert.IsTrue(Until(() => Drain(client, events).Count(e => (string)e["e"] == "away") >= 2, 2000), "away then back");
            Assert.AreEqual(1, events.Count(e => (string)e["e"] == "away" && (bool)e["away"]), "away once");
            Assert.AreEqual(1, events.Count(e => (string)e["e"] == "away" && !(bool)e["away"]), "back once");
            client.Leave();
        }

        static List<Dictionary<string, object>> Drain(HawkesNetClient c, List<Dictionary<string, object>> into)
        {
            while (c.Events.TryDequeue(out var e)) into.Add((Dictionary<string, object>)NetJson.Parse(e));
            return into;
        }

        static bool Until(Func<bool> test, int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { if (test()) return true; Thread.Sleep(10); }
            return false;
        }

        /// <summary>In-memory ticket + wire: hello with a resume token; resume URLs either succeed or refuse once.</summary>
        sealed class FakeHub
        {
            public bool RefuseResume;
            /// <summary>How a refused resume looks: 410 resume_gone, a proxy 502, or a socket that opens and closes before its hello.</summary>
            public string Refusal = "410";
            /// <summary>The network is gone: tickets and sockets both fail.</summary>
            public volatile bool Down;
            /// <summary>The ticket service is unreachable too (a whole network drop).</summary>
            public volatile bool TicketsDown;
            /// <summary>Use real waits for backoff (the 5 s drop test); otherwise they return at once.</summary>
            public bool RealDelay;
            public readonly List<int> Delays = new List<int>();
            public int TicketCount;
            public int RefusedResumes;
            public readonly List<string> Kinds = new List<string>();
            public FakeWire LastWire;
            readonly object gate = new object();

            public HawkesNetClient Make(int handle)
            {
                var o = new HawkesNetClientOptions
                {
                    project = "netdemo",
                    name = "Test",
                    AskTicket = ct =>
                    {
                        if (TicketsDown) return Task.FromException<Dictionary<string, object>>(new Exception("network down"));
                        int n;
                        lock (gate) { TicketCount++; n = TicketCount; }
                        return Task.FromResult(new Dictionary<string, object>
                        {
                            ["ticket"] = "t-" + n,
                            ["relayUrl"] = "ws://fake.example/v2",
                        });
                    },
                    ConnectWire = (uri, ct) => Open(uri, ct),
                    Delay = (ms, ct) =>
                    {
                        lock (gate) Delays.Add(ms);
                        return RealDelay ? Task.Delay(ms, ct) : Task.CompletedTask;
                    },
                };
                return new HawkesNetClient(handle, "t-reconnect-test", o);
            }

            Task<IHawkesNetWire> Open(Uri uri, CancellationToken ct)
            {
                var q = uri.Query ?? "";
                bool isResume = q.IndexOf("r=", StringComparison.Ordinal) >= 0;
                lock (gate) Kinds.Add(isResume ? "resume" : "ticket");
                if (Down) return Task.FromException<IHawkesNetWire>(new Exception("network down"));
                if (isResume && RefuseResume)
                {
                    Interlocked.Increment(ref RefusedResumes);
                    if (Refusal == "410") return Task.FromException<IHawkesNetWire>(new Exception("refused (410): resume_gone"));
                    if (Refusal == "502") return Task.FromException<IHawkesNetWire>(new Exception("refused (502): bad gateway"));
                    var dead = new FakeWire(); // opens, then closes before any hello
                    dead.Drop(1006, "");
                    return Task.FromResult((IHawkesNetWire)dead);
                }
                var w = new FakeWire();
                LastWire = w;
                w.Push(NetJson.Write(new Dictionary<string, object>
                {
                    ["t"] = "hello",
                    ["you"] = "p1",
                    ["name"] = "Test",
                    ["host"] = "p1",
                    ["now"] = HawkesNetClient.UnixMs(),
                    ["resume"] = "resume-token-1",
                    ["resumed"] = isResume,
                    ["peers"] = new List<object>(),
                }));
                return Task.FromResult((IHawkesNetWire)w);
            }
        }

        sealed class FakeWire : IHawkesNetWire
        {
            readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
            readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            volatile bool closed;
            public int CloseCode { get; private set; }
            public string CloseReason { get; private set; } = "";

            public void Push(string text)
            {
                if (closed) return;
                inbox.Enqueue(text);
                signal.Release();
            }

            public void PushFrame(Dictionary<string, object> m) => Push(NetJson.Write(m));

            public void Drop(int code, string reason)
            {
                CloseCode = code;
                CloseReason = reason ?? "";
                closed = true;
                signal.Release();
            }

            public Task SendTextAsync(string text, CancellationToken ct) => Task.CompletedTask;

            public async Task<string> ReceiveTextAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    if (inbox.TryDequeue(out var text)) return text;
                    if (closed) return null;
                    try { await signal.WaitAsync(ct); }
                    catch (OperationCanceledException) { return null; }
                }
                return null;
            }

            public void Dispose() => Drop(CloseCode != 0 ? CloseCode : 1000, CloseReason);
        }
    }
}
#endif
