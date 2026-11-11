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
                        int n;
                        lock (gate) { TicketCount++; n = TicketCount; }
                        return Task.FromResult(new Dictionary<string, object>
                        {
                            ["ticket"] = "t-" + n,
                            ["relayUrl"] = "ws://fake.example/v2",
                        });
                    },
                    ConnectWire = (uri, ct) => Open(uri, ct),
                    Delay = (ms, ct) => Task.CompletedTask, // no wall-clock backoff in unit tests
                };
                return new HawkesNetClient(handle, "t-reconnect-test", o);
            }

            Task<IHawkesNetWire> Open(Uri uri, CancellationToken ct)
            {
                var q = uri.Query ?? "";
                bool isResume = q.IndexOf("r=", StringComparison.Ordinal) >= 0;
                lock (gate) Kinds.Add(isResume ? "resume" : "ticket");
                if (isResume && RefuseResume)
                {
                    Interlocked.Increment(ref RefusedResumes);
                    return Task.FromException<IHawkesNetWire>(new Exception("ticket refused (410): resume_gone"));
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
