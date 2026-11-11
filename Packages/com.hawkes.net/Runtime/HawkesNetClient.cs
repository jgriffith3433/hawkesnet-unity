// Hawkes Net in the Editor and desktop builds (everything but WebGL, where the page's web SDK runs: HawkesNet.jslib).
// A plain .NET client of one room on the relay: a ticket over HTTPS, then a WebSocket speaking Hawkes Net's JSON protocol
// (subprotocol hawkes-net.v2: messages and poses as JSON, bundles as {"t":"b","m":[...]}). It hands HawkesNet.cs the same
// events and pose lists the WebGL bridge does, so the room, the lobby and the avatars work the same everywhere.
// No UnityEngine here (it is tested with plain dotnet).
//
// Tickets: a ticketUrl (your server asks hawkesgen.ai with your key), a key (servers, bots, tests: never ship one in a
// game), or, with neither, the localhost door: the Editor joins its project's test rooms (t-...) with no key.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hawkes
{
    /// <summary>One open wire to the relay (text frames). Production uses <see cref="ClientWebSocket"/>; tests inject a fake.</summary>
    public interface IHawkesNetWire : IDisposable
    {
        Task SendTextAsync(string text, CancellationToken ct);
        /// <summary>Next text frame, or null when the socket closed.</summary>
        Task<string> ReceiveTextAsync(CancellationToken ct);
        int CloseCode { get; }
        string CloseReason { get; }
    }

    /// <summary>What a managed room needs to join (HawkesNetOptions' fields, without Unity).</summary>
    public sealed class HawkesNetClientOptions
    {
        public string project = "", name = "", role = "player", endpoint = "https://net.hawkesgen.ai", ticketUrl = "", key = "", sub = "";
        /// <summary>The room's shape if this join creates it, as JSON (or empty).</summary>
        public string configJson = "";
        public bool ticketCredentials;
        /// <summary>Test hook: open a wire for this URI (ticket or resume query already on it). Null uses ClientWebSocket.</summary>
        public Func<Uri, CancellationToken, Task<IHawkesNetWire>> ConnectWire;
        /// <summary>Test hook: fetch a join ticket. Null uses HTTPS to the endpoint / ticketUrl.</summary>
        public Func<CancellationToken, Task<Dictionary<string, object>>> AskTicket;
        /// <summary>Test hook: delay (ms) before a reconnect try. Null uses Task.Delay. Pass a no-wait for unit tests.</summary>
        public Func<int, CancellationToken, Task> Delay;
    }

    /// <summary>One room on Hawkes Net's relay, outside a browser. Thread-safe: call it from the main thread, read
    /// <see cref="Events"/> and <see cref="TakePoses"/> there.</summary>
    public sealed class HawkesNetClient
    {
        public const string Version = "0.6.0";
        const int SendMs = 33, PingMs = 5000;

        readonly int handle;
        readonly string room;
        readonly HawkesNetClientOptions o;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly object gate = new object();
        readonly List<string> queue = new List<string>();
        readonly Dictionary<string, string> unreliable = new Dictionary<string, string>();
        string pose;
        string[] extraKeys = new string[0];
        int seq;
        IHawkesNetWire wire;
        double offset;
        readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        readonly Dictionary<string, string> poses = new Dictionary<string, string>();
        readonly HashSet<string> changed = new HashSet<string>();
        string self = "", host = "";
        string resume;
        string relayUrl;
        bool away, ready, closed, resuming, draining;
        int retry;

        sealed class Peer { public string id, name; public int ord; public bool away; }

        /// <summary>Events for HawkesNet.cs, in the WebGL bridge's JSON shape ({"h","e",...}).</summary>
        public readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();

        public HawkesNetClient(int handle, string room, HawkesNetClientOptions options)
        {
            this.handle = handle;
            this.room = room;
            o = options ?? new HawkesNetClientOptions();
            // Scenes saved before 0.6.0 hold the old live address, now gone: it means the default.
            if (string.IsNullOrEmpty(o.endpoint) || o.endpoint.TrimEnd('/') == "https://net.hawkesgen.com") o.endpoint = "https://net.hawkesgen.ai";
            if (string.IsNullOrEmpty(o.sub)) o.sub = Guid.NewGuid().ToString("N").Substring(0, 24);
        }

        public bool Ready => ready && !closed;

        /// <summary>The room's clock (ms), the same for everyone in it.</summary>
        public double Now() => UnixMs() + offset;

        public static double UnixMs() => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        public void Start() => Task.Run(Run);

        // ------------------------------------------------------------------ sending (queued; one bundle every 33 ms)

        /// <summary>dataJson: JSON (or empty for null). An unreliable message replaces one of its kind still queued.</summary>
        public bool Send(string kind, string dataJson, bool reliable, string to)
        {
            if (!Ready) return false;
            object data = null;
            if (!string.IsNullOrEmpty(dataJson)) { try { data = NetJson.Parse(dataJson); } catch { return false; } }
            return Queue(kind, data, reliable, to);
        }

        /// <summary>The game's own text as the data: it arrives as the same text.</summary>
        public bool SendText(string kind, string text, bool reliable, string to) => Ready && Queue(kind, text ?? "", reliable, to);

        bool Queue(string kind, object data, bool reliable, string to)
        {
            var m = new Dictionary<string, object> { ["t"] = "ev", ["name"] = kind.Length > 64 ? kind.Substring(0, 64) : kind, ["data"] = data };
            if (!string.IsNullOrEmpty(to)) m["to"] = to;
            lock (gate)
            {
                if (reliable) { m["seq"] = (double)(++seq); queue.Add(NetJson.Write(m)); }
                else unreliable[kind + (string.IsNullOrEmpty(to) ? "" : ">" + to)] = NetJson.Write(m);
            }
            return true;
        }

        /// <summary>My avatar (yaw in radians) and its extra values (a JSON object or empty). A key left out after being
        /// sent goes as null once, so the others drop it.</summary>
        public void Pose(float x, float y, float z, float yaw, string extraJson)
        {
            if (!Ready) return;
            var s = new Dictionary<string, object>();
            var keys = new List<string>();
            if (!string.IsNullOrEmpty(extraJson))
            {
                try
                {
                    if (NetJson.Parse(extraJson) is Dictionary<string, object> extra)
                        foreach (var kv in extra) if (kv.Key != "p" && kv.Key != "y") { s[kv.Key] = kv.Value; keys.Add(kv.Key); }
                }
                catch { /* not JSON: no extras */ }
            }
            foreach (var k in extraKeys) if (!s.ContainsKey(k)) s[k] = null;
            extraKeys = keys.ToArray();
            s["p"] = new List<object> { R(x, 1000), R(y, 1000), R(z, 1000) };
            s["y"] = R(yaw, 100);
            var m = new Dictionary<string, object> { ["t"] = "me", ["at"] = Math.Round(Now()), ["s"] = s };
            lock (gate) pose = NetJson.Write(m);
        }

        static double R(double v, double k) => Math.Round(v * k) / k;

        /// <summary>Say this player is away (or back).</summary>
        public void SetAway(bool on)
        {
            if (!Ready || on == away) return;
            away = on;
            lock (gate) queue.Add(NetJson.Write(new Dictionary<string, object> { ["t"] = "away", ["on"] = on }));
            Tell("selfaway", new Dictionary<string, object> { ["away"] = on });
        }

        /// <summary>The poses that changed since the last read, as {"items":[{"id","x","y","z","r","at","e"}]}, else null.</summary>
        public string TakePoses()
        {
            lock (poses)
            {
                if (changed.Count == 0) return null;
                var sb = new StringBuilder("{\"items\":[");
                bool first = true;
                foreach (var id in changed) { if (poses.TryGetValue(id, out var p)) { if (!first) sb.Append(','); sb.Append(p); first = false; } }
                changed.Clear();
                return sb.Append("]}").ToString();
            }
        }

        public void Leave()
        {
            if (closed) return;
            closed = true;
            try { FlushOnce().Wait(500); } catch { /* best effort */ }
            stop.Cancel();
            try { wire?.Dispose(); } catch { /* gone */ }
            wire = null;
        }

        // ------------------------------------------------------------------ the connection
        // Match JS SDK 1.4.1+: after an unexpected close try one resume; if that resume is refused (410 resume_gone /
        // proxy 502) or closes before its hello, drop the token and rejoin the same room with a fresh ticket. Backoff
        // only between full failures (no polling loop: each wait is scheduled from a close or a refused ticket).

        async Task Run()
        {
            while (!stop.IsCancellationRequested && !closed)
            {
                int closeCode = 0;
                string closeReason = "";
                bool opened = false;
                try
                {
                    string url;
                    if (!string.IsNullOrEmpty(resume) && !string.IsNullOrEmpty(relayUrl))
                    {
                        resuming = true;
                        url = relayUrl + (relayUrl.Contains("?") ? "&" : "?") + "r=" + Uri.EscapeDataString(resume);
                    }
                    else
                    {
                        resuming = false;
                        Dictionary<string, object> t;
                        try { t = await Ticket(); }
                        catch (Exception e)
                        {
                            if (!ready) { Tell("error", new Dictionary<string, object> { ["reason"] = "ticket", ["data"] = e.Message }); closed = true; return; }
                            // Ticket failure after we were in: backoff, then try again (full failure).
                            await Wait(draining ? Math.Min(800, 150 << Math.Min(3, retry++)) : 5000);
                            continue;
                        }
                        relayUrl = NetJson.Str(t, "relayUrl", "");
                        if (string.IsNullOrEmpty(relayUrl)) { Tell("error", new Dictionary<string, object> { ["reason"] = "ticket", ["data"] = "no relay in the ticket's answer" }); closed = true; return; }
                        resume = null;
                        url = relayUrl + (relayUrl.Contains("?") ? "&" : "?") + "t=" + Uri.EscapeDataString(NetJson.Str(t, "ticket", "")) + "&want=relay";
                    }
                    wire = await OpenWire(new Uri(url), stop.Token);
                    opened = true;
                    var sender = Task.Run(SendLoop);
                    await ReceiveLoop();
                    closeCode = wire != null ? wire.CloseCode : 0;
                    closeReason = wire != null ? (wire.CloseReason ?? "") : "";
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    Problem("relay-lost", e.Message);
                    // Connect refused before any frame (410 resume_gone, 502 while the node comes back): same as a
                    // resume try that closed before its hello.
                    if (resuming) { resume = null; resuming = false; }
                    if (!ready && !opened)
                    {
                        if (retry++ < 2) { await Wait(600); continue; }
                        Tell("error", new Dictionary<string, object> { ["reason"] = "refused", ["data"] = e.Message });
                        closed = true;
                        return;
                    }
                }
                finally
                {
                    try { wire?.Dispose(); } catch { /* gone */ }
                    wire = null;
                }
                if (closed || stop.IsCancellationRequested) return;
                if (closeCode == 4009) { Tell("problem", new Dictionary<string, object> { ["reason"] = "full", ["data"] = closeReason }); }
                if (closeCode == 4003 || closeCode == 4009 || (!ready && closeCode >= 4000))
                {
                    Tell("error", new Dictionary<string, object> { ["reason"] = closeCode == 4009 ? "full" : "refused", ["data"] = closeReason });
                    closed = true;
                    return;
                }
                if (!ready)
                {
                    if (retry++ < 2) { await Wait(600); continue; }
                    Tell("error", new Dictionary<string, object> { ["reason"] = "refused", ["data"] = string.IsNullOrEmpty(closeReason) ? ("closed " + closeCode) : closeReason });
                    closed = true;
                    return;
                }
                // A resume try that closed before its hello: the room is gone — don't resume again.
                if (resuming) { resume = null; resuming = false; }
                if (draining) resume = null;
                Status("reconnecting");
                int wait = draining
                    ? Math.Min(800, 150 << Math.Min(3, retry++))
                    : Math.Min(15000, 500 << Math.Min(5, retry++));
                await Wait(wait);
            }
        }

        async Task Wait(int ms)
        {
            try
            {
                if (o.Delay != null) await o.Delay(ms, stop.Token);
                else await Task.Delay(ms, stop.Token);
            }
            catch { /* stopping */ }
        }

        async Task<IHawkesNetWire> OpenWire(Uri uri, CancellationToken ct)
        {
            if (o.ConnectWire != null) return await o.ConnectWire(uri, ct);
            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("hawkes-net.v2");
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await ws.ConnectAsync(uri, ct);
            return new ClientWebSocketWire(ws);
        }

        async Task<Dictionary<string, object>> Ticket()
        {
            if (o.AskTicket != null) return await o.AskTicket(stop.Token);
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                HttpRequestMessage req;
                if (!string.IsNullOrEmpty(o.ticketUrl))
                {
                    req = new HttpRequestMessage(HttpMethod.Post, o.ticketUrl);
                    req.Content = new StringContent(NetJson.Write(new Dictionary<string, object> { ["room"] = room, ["name"] = string.IsNullOrEmpty(o.name) ? null : o.name }), Encoding.UTF8, "application/json");
                }
                else
                {
                    req = new HttpRequestMessage(HttpMethod.Post, o.endpoint.TrimEnd('/') + "/v1/net/ticket");
                    var body = new Dictionary<string, object>
                    {
                        ["project"] = o.project, ["room"] = room, ["name"] = string.IsNullOrEmpty(o.name) ? null : o.name, ["sub"] = o.sub,
                        ["role"] = string.IsNullOrEmpty(o.role) ? "player" : o.role, ["transport"] = "relay",
                        ["client"] = new Dictionary<string, object> { ["sdk"] = "unity-managed/" + Version, ["protocols"] = new List<object> { 2.0 } },
                    };
                    if (!string.IsNullOrEmpty(o.configJson)) { try { body["config"] = NetJson.Parse(o.configJson); } catch { /* no shape */ } }
                    req.Content = new StringContent(NetJson.Write(body), Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(o.key)) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + o.key);
                    // No key: the localhost door, which lets a project's test rooms in (the Editor's).
                    else req.Headers.TryAddWithoutValidation("Origin", "http://localhost");
                }
                req.Headers.TryAddWithoutValidation("X-Hawkes-Client", "unity-managed/" + Version);
                var res = await http.SendAsync(req, stop.Token);
                var text = await res.Content.ReadAsStringAsync();
                object j = null;
                try { j = NetJson.Parse(text); } catch { /* not JSON */ }
                if (!res.IsSuccessStatusCode) throw new Exception($"ticket refused ({(int)res.StatusCode}): {NetJson.Str(j, "error", text.Length > 200 ? text.Substring(0, 200) : text)}");
                return j as Dictionary<string, object> ?? throw new Exception("the ticket's answer is not JSON");
            }
        }

        async Task SendLoop()
        {
            double pingAt = 0;
            while (!stop.IsCancellationRequested && wire != null)
            {
                try
                {
                    if (UnixMs() - pingAt > PingMs) { pingAt = UnixMs(); lock (gate) queue.Add("{\"t\":\"ping\",\"at\":" + Math.Round(UnixMs()).ToString(CultureInfo.InvariantCulture) + "}"); }
                    await FlushOnce();
                    await Task.Delay(SendMs, stop.Token);
                }
                catch { return; }
            }
        }

        readonly SemaphoreSlim sending = new SemaphoreSlim(1, 1);
        async Task FlushOnce()
        {
            List<string> items;
            lock (gate)
            {
                if (queue.Count == 0 && unreliable.Count == 0 && pose == null) return;
                items = new List<string>(queue);
                items.AddRange(unreliable.Values);
                if (pose != null) items.Add(pose);
                queue.Clear(); unreliable.Clear(); pose = null;
            }
            if (wire == null) return;
            // Bundles of at most 200 items (the relay reads 256).
            for (int i = 0; i < items.Count; i += 200)
            {
                var part = items.GetRange(i, Math.Min(200, items.Count - i));
                var text = "{\"t\":\"b\",\"m\":[" + string.Join(",", part) + "]}";
                await sending.WaitAsync();
                try { await wire.SendTextAsync(text, stop.Token); }
                finally { sending.Release(); }
            }
        }

        async Task ReceiveLoop()
        {
            while (wire != null && !stop.IsCancellationRequested)
            {
                string text;
                try { text = await wire.ReceiveTextAsync(stop.Token); }
                catch { return; }
                if (text == null) return;
                object m;
                try { m = NetJson.Parse(text); } catch { continue; }
                try { FromRoom(m); }
                catch (Exception e) { Problem("bad-message", e.Message); }
            }
        }

        // ------------------------------------------------------------------ what the room says

        void FromRoom(object raw)
        {
            if (!(raw is Dictionary<string, object> m)) return;
            var t = NetJson.Str(m, "t", "");
            var c = NetJson.Str(m, "c", "");
            // Control frames the node pushes without a "t" (drain before a restart): skip the doomed resume.
            if (string.IsNullOrEmpty(t) && c == "draining")
            {
                if (closed || draining) return;
                draining = true;
                resume = null;
                retry = 0;
                try { wire?.Dispose(); } catch { /* close wakes ReceiveLoop; Run() clears resume and rejoins */ }
                return;
            }
            if (t == "b") { foreach (var x in NetJson.Arr(m, "m") ?? new List<object>()) FromRoom(x); return; }
            switch (t)
            {
                case "hello": Hello(m); return;
                case "pong":
                {
                    double at = NetJson.Num(m, "at"), now = NetJson.Num(m, "now"), rtt = UnixMs() - at;
                    if (rtt >= 0 && rtt < 30000) offset = now + rtt / 2 - UnixMs();
                    return;
                }
                case "join":
                {
                    var p = AddPeer(m);
                    if (p != null) Tell("join", new Dictionary<string, object> { ["peer"] = p.id, ["name"] = p.name, ["ord"] = (double)p.ord, ["away"] = p.away });
                    return;
                }
                case "leave":
                {
                    var id = NetJson.Str(m, "c", "");
                    if (!peers.TryGetValue(id, out var p)) return;
                    peers.Remove(id);
                    lock (poses) { poses.Remove(id); changed.Remove(id); }
                    Tell("leave", new Dictionary<string, object> { ["peer"] = id, ["name"] = p.name ?? "", ["reason"] = NetJson.Str(m, "why", "left") });
                    return;
                }
                case "host":
                {
                    var was = host;
                    host = NetJson.Str(m, "c", "");
                    var reason = peers.TryGetValue(was, out var wp) && wp.away || (was == self && away) ? "host away" : "host left";
                    Tell("host", new Dictionary<string, object> { ["peer"] = host, ["reason"] = reason });
                    return;
                }
                case "away":
                {
                    var id = NetJson.Str(m, "c", "");
                    if (!peers.TryGetValue(id, out var p)) return;
                    p.away = NetJson.Bool(m, "on");
                    Tell("away", new Dictionary<string, object> { ["peer"] = id, ["name"] = p.name ?? "", ["away"] = p.away });
                    return;
                }
                case "me":
                {
                    var id = NetJson.Str(m, "c", "");
                    if (!(NetJson.Obj(m, "s") is Dictionary<string, object> s)) return;
                    var pos = NetJson.Arr(s, "p");
                    if (pos == null || pos.Count < 3) return;
                    var extra = new Dictionary<string, object>();
                    foreach (var kv in s) if (kv.Key != "p" && kv.Key != "y" && kv.Value != null) extra[kv.Key] = kv.Value;
                    var item = new Dictionary<string, object>
                    {
                        ["id"] = id, ["x"] = Convert.ToDouble(pos[0]), ["y"] = Convert.ToDouble(pos[1]), ["z"] = Convert.ToDouble(pos[2]),
                        ["r"] = NetJson.Num(s, "y"), ["at"] = NetJson.Num(m, "at"), ["e"] = extra.Count > 0 ? NetJson.Write(extra) : "",
                    };
                    lock (poses) { poses[id] = NetJson.Write(item); changed.Add(id); }
                    return;
                }
                case "ev":
                {
                    var from = NetJson.Str(m, "c", "");
                    var name = NetJson.Str(m, "name", "");
                    if (name.StartsWith("_")) return; // the SDK's own (synced state): not the game's
                    m.TryGetValue("data", out var data);
                    var text = data is string;
                    var who = from != null && peers.TryGetValue(from, out var p) ? p.name : "";
                    Tell("msg", new Dictionary<string, object>
                    {
                        ["kind"] = name, ["data"] = text ? (string)data : NetJson.Write(data), ["text"] = text, ["peer"] = from ?? "", ["name"] = who ?? "",
                        ["reliable"] = m.ContainsKey("seq"),
                    });
                    return;
                }
            }
        }

        void Hello(Dictionary<string, object> m)
        {
            var tok = NetJson.Str(m, "resume", "");
            if (!string.IsNullOrEmpty(tok)) resume = tok;
            retry = 0;
            resuming = false;
            draining = false;
            self = NetJson.Str(m, "you", "");
            host = NetJson.Str(m, "host", "");
            var now = NetJson.Num(m, "now");
            if (now > 0 && offset == 0) offset = now - UnixMs();
            peers.Clear();
            lock (poses) { poses.Clear(); changed.Clear(); }
            var list = new List<object>();
            int ord = 0;
            foreach (var x in NetJson.Arr(m, "peers") ?? new List<object>())
            {
                if (NetJson.Str(x, "c", "") == self) { ord = (int)NetJson.Num(x, "ord"); continue; }
                var p = AddPeer(x as Dictionary<string, object>);
                if (p != null) list.Add(new Dictionary<string, object> { ["peer"] = p.id, ["name"] = p.name ?? "", ["ord"] = (double)p.ord, ["away"] = p.away });
            }
            if (away) lock (gate) queue.Add("{\"t\":\"away\",\"on\":true}");
            var first = !ready;
            ready = true;
            Status("online");
            if (first) Tell("ready", new Dictionary<string, object> { ["peer"] = self, ["name"] = NetJson.Str(m, "name", o.name), ["transport"] = "relay", ["host"] = host, ["ord"] = (double)ord, ["data"] = NetJson.Write(list) });
        }

        /// <summary>ClientWebSocket as <see cref="IHawkesNetWire"/> (Editor / desktop).</summary>
        sealed class ClientWebSocketWire : IHawkesNetWire
        {
            readonly ClientWebSocket ws;
            readonly byte[] buf = new byte[64 * 1024];
            readonly List<byte> msg = new List<byte>();
            public ClientWebSocketWire(ClientWebSocket ws) { this.ws = ws; }
            public int CloseCode => ws.CloseStatus.HasValue ? (int)ws.CloseStatus.Value : 0;
            public string CloseReason => ws.CloseStatusDescription ?? "";
            public async Task SendTextAsync(string text, CancellationToken ct)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            public async Task<string> ReceiveTextAsync(CancellationToken ct)
            {
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return null;
                    for (int i = 0; i < r.Count; i++) msg.Add(buf[i]);
                    if (!r.EndOfMessage) continue;
                    var text = Encoding.UTF8.GetString(msg.ToArray());
                    msg.Clear();
                    if (r.MessageType != WebSocketMessageType.Text) continue; // binary is the web SDK's
                    return text;
                }
                return null;
            }
            public void Dispose()
            {
                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                        ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(200);
                }
                catch { /* gone */ }
                try { ws.Dispose(); } catch { /* gone */ }
            }
        }

        Peer AddPeer(Dictionary<string, object> x)
        {
            if (x == null) return null;
            var id = NetJson.Str(x, "c", "");
            if (string.IsNullOrEmpty(id) || id == self) return null;
            if (!peers.TryGetValue(id, out var p)) { p = new Peer { id = id }; peers[id] = p; }
            p.name = NetJson.Str(x, "name", p.name ?? "");
            p.ord = (int)NetJson.Num(x, "ord", p.ord);
            p.away = NetJson.Bool(x, "away");
            return p;
        }

        void Status(string s) => Tell("status", new Dictionary<string, object> { ["status"] = s, ["transport"] = "relay" });
        void Problem(string code, string detail) => Tell("problem", new Dictionary<string, object> { ["reason"] = code, ["data"] = detail ?? "" });

        void Tell(string e, Dictionary<string, object> x)
        {
            x["h"] = (double)handle;
            x["e"] = e;
            Events.Enqueue(NetJson.Write(x));
        }
    }

    /// <summary>A small JSON reader and writer: objects are Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string, bool and null.</summary>
    public static class NetJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = Read(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value");
            return v;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static Dictionary<string, object> Obj(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> Arr(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as List<object> : null;
        public static string Str(object o, string key, string fallback = null) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;
        public static double Num(object o, string key, double fallback = 0) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is double n ? n : fallback;
        public static bool Bool(object o, string key) => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is bool b && b;

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Read(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var obj = new Dictionary<string, object>();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                while (true)
                {
                    Skip(s, ref i);
                    var key = ReadString(s, ref i);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':'");
                    i++;
                    obj[key] = Read(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return obj; }
                    throw new FormatException("Expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var arr = new List<object>();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return arr; }
                while (true)
                {
                    arr.Add(Read(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return arr; }
                    throw new FormatException("Expected ',' or ']'");
                }
            }
            if (c == '"') return ReadString(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException($"Unexpected character '{c}' in JSON");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("Expected a string");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw new FormatException("Bad escape in a JSON string");
                }
            }
            throw new FormatException("Unterminated JSON string");
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: WriteValue(sb, (double)f); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case Dictionary<string, object> obj:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in obj) { if (!first) sb.Append(','); first = false; WriteString(sb, kv.Key); sb.Append(':'); WriteValue(sb, kv.Value); }
                    sb.Append('}');
                    return;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var x in list) { if (!first) sb.Append(','); first = false; WriteValue(sb, x); }
                    sb.Append(']');
                    return;
                }
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); return;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
