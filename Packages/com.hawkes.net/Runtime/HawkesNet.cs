using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Hawkes
{
    /// <summary>How to join a room. Give either TicketUrl (your server asks hawkesgen.com for tickets with your API key:
    /// POST /v1/net/rooms/ticket) or Project (a Hawkes Net project whose sites may ask without a key).</summary>
    [Serializable]
    public class HawkesNetOptions
    {
        /// <summary>Your server's ticket route. The page POSTs { room, name } to it and expects the answer it got from
        /// hawkesgen.com back as JSON.</summary>
        public string ticketUrl = "";
        /// <summary>Send cookies with the ticket request (your ticket route is on another site that signs players in).</summary>
        public bool ticketCredentials;
        /// <summary>A Hawkes Net project that hands out tickets to pages on its own sites (no key).</summary>
        public string project = "";
        public string name = "";
        /// <summary>"relay" (the default), or "auto" (peer to peer while small, then the relay) where the project allows
        /// peer to peer (a setting on net.hawkesgen.com, off by default).</summary>
        public string transport = "relay";
        /// <summary>"player" or "spectator".</summary>
        public string role = "player";
        /// <summary>The Hawkes Net service, when not https://net.hawkesgen.com.</summary>
        public string endpoint = "";
        /// <summary>Where the SDK is loaded from.</summary>
        public string sdkUrl = "https://net.hawkesgen.com/sdk/hawkes-net@1.mjs";
        /// <summary>Leave the room when the tab is closed or hidden for long (the web default). Off for games that
        /// keep playing in the background.</summary>
        public bool leaveOnHide = true;
        public bool log = true;
        /// <summary>Outside WebGL only (the Editor, desktop builds): a Hawkes Gen API key for servers, bots and tests. Never
        /// ship one in a game: players' builds use ticketUrl. With neither, the Editor joins its project's test rooms
        /// (t-...) through the localhost door.</summary>
        public string key = "";
        /// <summary>The room's shape if this join creates it, as JSON (e.g. {"maxPlayers":8}); empty for the project's.</summary>
        public string configJson = "";

        public HawkesNetOptions Copy() => (HawkesNetOptions)MemberwiseClone();
    }

    /// <summary>A peer's avatar: where it is, which way it faces (yaw in degrees around Y, as in Unity), when it was there
    /// (the room's clock, ms) and the extra values it sent with it (a JSON object, "" for none).</summary>
    public struct HawkesNetPose
    {
        public Vector3 position;
        public float yaw;
        public double at;
        public string extra;
    }

    /// <summary>A room you joined. Events arrive on the main thread, during Update.</summary>
    public class HawkesNetRoom
    {
        internal readonly int handle;
        internal HawkesNetRoom(int handle, string name) { this.handle = handle; Name = name; }

        public string Name { get; }
        /// <summary>Your peer id in the room (once Ready).</summary>
        public string Id { get; internal set; } = "";
        /// <summary>Your place in the join order (the oldest player is the host).</summary>
        public int Order { get; internal set; }
        public string Host { get; internal set; } = "";
        public bool IsHost => Id != "" && Id == Host;
        /// <summary>"p2p" or "relay".</summary>
        public string Transport { get; internal set; } = "";
        /// <summary>"connecting", "online", "reconnecting", "closed".</summary>
        public string Status { get; internal set; } = "connecting";
        public bool Ready { get; internal set; }
        /// <summary>Why the room closed (a refused join's reason, "left"...), "" while open.</summary>
        public string ClosedBecause { get; internal set; } = "";
        /// <summary>Everyone else in the room: peer id to name.</summary>
        public readonly Dictionary<string, string> Peers = new Dictionary<string, string>();
        /// <summary>Everyone else's place in the join order.</summary>
        public readonly Dictionary<string, int> PeerOrder = new Dictionary<string, int>();
        /// <summary>Everyone else's newest avatar pose.</summary>
        public readonly Dictionary<string, HawkesNetPose> Poses = new Dictionary<string, HawkesNetPose>();
        /// <summary>The peers whose page is in the background (a hidden tab, a minimised or covered window).</summary>
        public readonly HashSet<string> Away = new HashSet<string>();
        /// <summary>This game's own page is in the background and the room knows it.</summary>
        public bool IsAway { get; internal set; }

        public event Action<HawkesNetRoom> OnReady;
        /// <summary>A peer arrived (id, name).</summary>
        public event Action<string, string> OnPeerJoin;
        /// <summary>A peer left (id, reason).</summary>
        public event Action<string, string> OnPeerLeave;
        /// <summary>The host changed (new host id). A host whose page goes into the background hands the room to a present
        /// player (HostReason "host away").</summary>
        public event Action<string> OnHost;
        /// <summary>Why the host last changed: "first", "host left" or "host away".</summary>
        public string HostReason { get; internal set; } = "";
        /// <summary>A peer's page went into the background or came back (id, away).</summary>
        public event Action<string, bool> OnPeerAway;
        /// <summary>This page went into the background (after 2 s) or came back.</summary>
        public event Action<bool> OnAway;
        /// <summary>The room moved between peer to peer and the relay (new transport, why). Nothing to do: it is told.</summary>
        public event Action<string, string> OnTransport;
        /// <summary>The connection's status changed ("online", "reconnecting", "closed").</summary>
        public event Action<string> OnStatus;
        /// <summary>A problem (code, detail): "full", "capped", "relay-lost", "ticket"... The join failed if Ready is false.</summary>
        public event Action<string, string> OnProblem;
        /// <summary>Any message (kind, data, from peer id): data is the JSON, or the text a SendText sent.</summary>
        public event Action<string, string, string> OnMessage;
        /// <summary>A peer's avatar moved (id, its newest pose). Read in the frame it arrived; Poses keeps the newest.</summary>
        public event Action<string, HawkesNetPose> OnPose;

        readonly Dictionary<string, Action<string, string>> handlers = new Dictionary<string, Action<string, string>>();

        /// <summary>Messages of one kind: (JSON data, or SendText's text; from peer id). Parse JSON with JsonUtility.</summary>
        public void On(string kind, Action<string, string> handler)
        {
            handlers.TryGetValue(kind, out var h);
            handlers[kind] = h + handler;
        }

        /// <summary>Messages of one kind as a JsonUtility type.</summary>
        public void On<T>(string kind, Action<T, string> handler)
        {
            On(kind, (json, from) =>
            {
                T v;
                try { v = JsonUtility.FromJson<T>(json); }
                catch (Exception e) { Debug.LogWarning("[HawkesNet] " + kind + ": " + e.Message); return; }
                handler(v, from);
            });
        }

        /// <summary>Send to everyone. reliable: once and in order (default); false: may drop, and a newer one of the same
        /// kind replaces one still queued, for things sent often. to: one peer id. data: an object for JsonUtility, a JSON
        /// string, or null.</summary>
        public bool Send(string kind, object data = null, bool reliable = true, string to = "")
        {
            string json = data == null ? "" : data is string s ? s : JsonUtility.ToJson(data);
            return HawkesNet.Send(handle, kind, json, reliable, to ?? "");
        }

        /// <summary>Send the game's own text (any string): it arrives as the same text.</summary>
        public bool SendText(string kind, string text, bool reliable = true, string to = "") => HawkesNet.SendText(handle, kind, text ?? "", reliable, to ?? "");

        /// <summary>Your avatar, as often as you like: the newest is sent about 30 times a second, and the relay sends the
        /// far ones less often. Yaw in degrees. extra: small values that ride with it (a JSON object or a JsonUtility
        /// object; a key you stop sending is dropped for the others too).</summary>
        public void Pose(Vector3 position, float yaw, object extra = null)
        {
            string json = extra == null ? "" : extra is string s ? s : JsonUtility.ToJson(extra);
            HawkesNet.Pose(handle, position, yaw, json);
        }

        /// <summary>The room's clock (milliseconds, the same for everyone in it).</summary>
        public double Now => HawkesNet.Now(handle);

        /// <summary>Ask the room to move to the relay now.</summary>
        public void Migrate() => HawkesNet.Migrate(handle);

        /// <summary>Say you're away (or back) now: a game paused in its own menu. The page's own state (in the background
        /// or not) takes over again the next time it changes.</summary>
        public void SetAway(bool away) => HawkesNet.SetAway(handle, away);

        public void Leave() => HawkesNet.Leave(this);

        internal void Dispatch(HawkesNet.Event e)
        {
            switch (e.e)
            {
                case "ready":
                    Id = e.peer; Order = e.ord; Transport = e.transport; Host = e.host; Ready = true; Status = "online";
                    foreach (var p in HawkesNet.ParsePeers(e.data)) { Peers[p.peer] = p.name; PeerOrder[p.peer] = p.ord; if (p.away) Away.Add(p.peer); else Away.Remove(p.peer); }
                    OnReady?.Invoke(this);
                    break;
                case "join":
                    Peers[e.peer] = e.name;
                    PeerOrder[e.peer] = e.ord;
                    OnPeerJoin?.Invoke(e.peer, e.name);
                    break;
                case "leave":
                    Peers.Remove(e.peer); PeerOrder.Remove(e.peer); Poses.Remove(e.peer); Away.Remove(e.peer);
                    OnPeerLeave?.Invoke(e.peer, e.reason);
                    break;
                case "host":
                    Host = e.peer;
                    HostReason = e.reason;
                    OnHost?.Invoke(e.peer);
                    break;
                case "away":
                    if (e.away) Away.Add(e.peer); else Away.Remove(e.peer);
                    OnPeerAway?.Invoke(e.peer, e.away);
                    break;
                case "selfaway":
                    IsAway = e.away;
                    OnAway?.Invoke(e.away);
                    break;
                case "transport":
                    Transport = e.transport;
                    OnTransport?.Invoke(e.transport, e.reason);
                    break;
                case "status":
                    Status = e.status;
                    if (!string.IsNullOrEmpty(e.transport)) Transport = e.transport;
                    OnStatus?.Invoke(e.status);
                    break;
                case "problem":
                case "error":
                    // A refused or failed join closes the room: its reason first, then the status.
                    if (e.e == "error") { Status = "closed"; ClosedBecause = string.IsNullOrEmpty(e.reason) ? "error" : e.reason; }
                    OnProblem?.Invoke(e.reason, e.data);
                    if (e.e == "error") OnStatus?.Invoke(Status);
                    break;
                case "msg":
                    OnMessage?.Invoke(e.kind, e.data, e.peer);
                    if (handlers.TryGetValue(e.kind, out var h)) h?.Invoke(e.data, e.peer);
                    break;
            }
        }

        internal void ReadPoses(string json)
        {
            HawkesNet.PoseList list;
            try { list = JsonUtility.FromJson<HawkesNet.PoseList>(json); }
            catch (Exception x) { Debug.LogWarning("[HawkesNet] poses: " + x.Message); return; }
            if (list?.items == null) return;
            foreach (var p in list.items)
            {
                if (p == null || string.IsNullOrEmpty(p.id) || !Peers.ContainsKey(p.id)) continue;
                var pose = new HawkesNetPose { position = new Vector3(p.x, p.y, p.z), yaw = p.r * Mathf.Rad2Deg, at = p.at, extra = p.e ?? "" };
                Poses[p.id] = pose;
                if (OnPose != null)
                {
                    try { OnPose(p.id, pose); }
                    catch (Exception x) { Debug.LogException(x); }
                }
            }
        }
    }

    /// <summary>
    /// Hawkes Net multiplayer for Unity (https://net.hawkesgen.com). In a WebGL build the web SDK runs in the page and this
    /// is its C# face; in the Editor and desktop builds a managed client (HawkesNetClient) joins the same rooms on the relay.
    /// HawkesNet.Join("lobby", new HawkesNetOptions { ticketUrl = "https://mygame.example/ticket", name = "Ada" })
    /// gives a room; handle OnReady, On("chat", ...), read Poses (or OnPose) each frame, call Pose(...) with yours. Most
    /// games want the shared-room layer on top of this, HawkesNetLobby, and many the drop-in avatars, HawkesNetAvatars.
    /// </summary>
    public static class HawkesNet
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void HawkesNet_Join(int handle, string room, string optionsJson, string receiver);
        [DllImport("__Internal")] static extern int HawkesNet_Send(int handle, string kind, string dataJson, bool reliable, string to);
        [DllImport("__Internal")] static extern int HawkesNet_SendText(int handle, string kind, string text, bool reliable, string to);
        [DllImport("__Internal")] static extern void HawkesNet_Pose(int handle, float x, float y, float z, float yaw, string extraJson);
        [DllImport("__Internal")] static extern string HawkesNet_Poses(int handle);
        [DllImport("__Internal")] static extern double HawkesNet_Now(int handle);
        [DllImport("__Internal")] static extern void HawkesNet_Migrate(int handle);
        [DllImport("__Internal")] static extern void HawkesNet_Leave(int handle);
        [DllImport("__Internal")] static extern void HawkesNet_Count(int id, string endpoint, string project, string rooms, string receiver);
        [DllImport("__Internal")] static extern void HawkesNet_SetAway(int handle, bool on);
        [DllImport("__Internal")] static extern int HawkesNet_RunInBackground(bool on, string sdkUrl);
        /// <summary>A WebGL build: the page's SDK carries the rooms. Elsewhere the managed client does.</summary>
        public static readonly bool Live = true;
#else
        static void HawkesNet_Join(int handle, string room, string optionsJson, string receiver) { }
        static int HawkesNet_Send(int handle, string kind, string dataJson, bool reliable, string to) => 0;
        static int HawkesNet_SendText(int handle, string kind, string text, bool reliable, string to) => 0;
        static void HawkesNet_Pose(int handle, float x, float y, float z, float yaw, string extraJson) { }
        static string HawkesNet_Poses(int handle) => null;
        static double HawkesNet_Now(int handle) => UnixMs();
        static void HawkesNet_Migrate(int handle) { }
        static void HawkesNet_Leave(int handle) { }
        static void HawkesNet_Count(int id, string endpoint, string project, string rooms, string receiver) { }
        static void HawkesNet_SetAway(int handle, bool on) { }
        static int HawkesNet_RunInBackground(bool on, string sdkUrl) => 0;
        public static readonly bool Live = false;
#endif

        const string ReceiverName = "HawkesNet";
        static readonly Dictionary<int, HawkesNetRoom> rooms = new Dictionary<int, HawkesNetRoom>();
        /// <summary>Outside WebGL: each room's managed client, by handle.</summary>
        static readonly Dictionary<int, HawkesNetClient> managed = new Dictionary<int, HawkesNetClient>();
        static readonly Dictionary<int, Action<bool, Dictionary<string, int>>> counts = new Dictionary<int, Action<bool, Dictionary<string, int>>>();
        static int next;
        static Receiver receiver;

        static bool runInBackground;
        /// <summary>
        /// Keep the game running while its page is in the background (a hidden tab, a minimised or covered window: the
        /// browser stops Unity's frames there). On, Unity's main loop runs about 60 times a second from Hawkes Net's
        /// background clock until frames come back, so Update keeps going: a host's simulation, a turn timer, a fight. Off
        /// (the default) the game pauses in the background and the room marks the player away (Away, OnPeerAway), and an
        /// away host hands the room to a present player. It also sets Application.runInBackground, so Unity itself doesn't
        /// pause when the page loses focus. WebGL builds only. Hawkes Gen docs/net.md "In the background".
        /// </summary>
        public static bool RunInBackground
        {
            get => runInBackground;
            set
            {
                runInBackground = value;
                Application.runInBackground = value;
                if (Live && HawkesNet_RunInBackground(value, SdkUrl) == 0 && value) Debug.LogWarning("[HawkesNet] RunInBackground: Unity's main loop was not found in this build; the game pauses in the background");
            }
        }
        /// <summary>Where the web SDK loads from (RunInBackground; a room's own sdkUrl is in its options).</summary>
        public static string SdkUrl = "https://net.hawkesgen.com/sdk/hawkes-net@1.mjs";

        /// <summary>Milliseconds since 1970 on this machine's clock (a room's Now is the shared one).</summary>
        public static double UnixMs() => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        /// <summary>Join a room (1 to 64 of letters, digits and / _ . -). The room is ready when OnReady fires.</summary>
        public static HawkesNetRoom Join(string room, HawkesNetOptions options)
        {
            options = options ?? new HawkesNetOptions();
            Ensure();
            var r = new HawkesNetRoom(++next, room);
            rooms[r.handle] = r;
            if (!Live)
            {
                var c = new HawkesNetClient(r.handle, room, new HawkesNetClientOptions
                {
                    project = options.project, name = options.name, role = options.role, ticketUrl = options.ticketUrl, key = options.key,
                    endpoint = string.IsNullOrEmpty(options.endpoint) ? "https://net.hawkesgen.com" : options.endpoint,
                    configJson = options.configJson, ticketCredentials = options.ticketCredentials, sub = DeviceSub(),
                });
                managed[r.handle] = c;
                c.Start();
                return r;
            }
            HawkesNet_Join(r.handle, room, JsonUtility.ToJson(options), ReceiverName);
            return r;
        }

        /// <summary>
        /// How many players are in each of these rooms of a project with its own sites (its public counts: rooms anyone on
        /// its sites could join anyway, so only the numbers). done(ok, room to players) runs on the main thread; outside a
        /// WebGL build ok is false.
        /// </summary>
        public static void Count(string project, IList<string> roomNames, Action<bool, Dictionary<string, int>> done, string endpoint = "")
        {
            Ensure();
            int id = ++next;
            counts[id] = done;
            if (!Live) { CountManaged(id, endpoint, project, roomNames); return; }
            HawkesNet_Count(id, endpoint ?? "", project ?? "", string.Join(",", roomNames ?? new string[0]), ReceiverName);
        }

        static HawkesNetClient M(int h) => managed.TryGetValue(h, out var c) ? c : null;
        internal static bool Send(int h, string kind, string json, bool reliable, string to) => Live ? HawkesNet_Send(h, kind, json, reliable, to) == 1 : M(h)?.Send(kind, json, reliable, to) == true;
        internal static bool SendText(int h, string kind, string text, bool reliable, string to) => Live ? HawkesNet_SendText(h, kind, text, reliable, to) == 1 : M(h)?.SendText(kind, text, reliable, to) == true;
        internal static void Pose(int h, Vector3 p, float yaw, string extra) // radians on the wire
        {
            if (Live) HawkesNet_Pose(h, p.x, p.y, p.z, yaw * Mathf.Deg2Rad, extra ?? "");
            else M(h)?.Pose(p.x, p.y, p.z, yaw * Mathf.Deg2Rad, extra ?? "");
        }
        internal static double Now(int h) => Live ? HawkesNet_Now(h) : M(h)?.Now() ?? UnixMs();
        internal static void Migrate(int h) { if (Live) HawkesNet_Migrate(h); } // (managed rooms are on the relay already)
        internal static void SetAway(int h, bool away) { if (Live) HawkesNet_SetAway(h, away); else M(h)?.SetAway(away); }
        internal static void Leave(HawkesNetRoom r)
        {
            rooms.Remove(r.handle);
            if (Live) HawkesNet_Leave(r.handle);
            else if (managed.TryGetValue(r.handle, out var c)) { managed.Remove(r.handle); c.Leave(); }
            r.Status = "closed";
            if (r.ClosedBecause.Length == 0) r.ClosedBecause = "left";
        }

        /// <summary>A stable id for this device (PlayerPrefs), so a player keeps their place across reconnects.</summary>
        static string DeviceSub()
        {
            const string k = "hawkesNet.sub";
            var v = PlayerPrefs.GetString(k, "");
            if (v.Length == 0) { v = Guid.NewGuid().ToString("N").Substring(0, 24); PlayerPrefs.SetString(k, v); }
            return v;
        }

        static readonly System.Collections.Concurrent.ConcurrentQueue<string> managedCounts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        static void CountManaged(int id, string endpoint, string project, IList<string> roomNames)
        {
            var url = (string.IsNullOrEmpty(endpoint) ? "https://net.hawkesgen.com" : endpoint.TrimEnd('/')) + "/v1/net/counts?project=" + Uri.EscapeDataString(project ?? "") + "&rooms=" + Uri.EscapeDataString(string.Join(",", roomNames ?? new string[0]));
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        var res = await http.GetAsync(url);
                        var text = await res.Content.ReadAsStringAsync();
                        var j = NetJson.Parse(text);
                        var answer = new Dictionary<string, object> { ["id"] = (double)id, ["ok"] = res.IsSuccessStatusCode };
                        if (res.IsSuccessStatusCode) answer["rooms"] = NetJson.Obj(j, "rooms") ?? new Dictionary<string, object>();
                        else answer["error"] = NetJson.Str(j, "error", "refused");
                        managedCounts.Enqueue(NetJson.Write(answer));
                    }
                }
                catch (Exception e) { managedCounts.Enqueue(NetJson.Write(new Dictionary<string, object> { ["id"] = (double)id, ["ok"] = false, ["error"] = e.Message })); }
            });
        }

        static void Ensure()
        {
            if (receiver) return;
            var go = new GameObject(ReceiverName);
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            receiver = go.AddComponent<Receiver>();
        }

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable]
        internal class Event
        {
            public int h;
            public string e = "", kind = "", data = "", peer = "", name = "", reason = "", transport = "", host = "", status = "";
            public bool reliable, text, away;
            public int ord;
        }

        [Serializable] internal class PeerInfo { public string peer = "", name = ""; public int ord; public bool away; }
        [Serializable] class PeerList { public PeerInfo[] items; }
        [Serializable] internal class PoseItem { public string id = "", e = ""; public float x, y, z, r; public double at; }
        [Serializable] internal class PoseList { public PoseItem[] items; }
        [Serializable] class CountAnswer { public int id; public bool ok; public string error = ""; }
#pragma warning restore 0649

        internal static PeerInfo[] ParsePeers(string json)
        {
            if (string.IsNullOrEmpty(json)) return new PeerInfo[0];
            try { return JsonUtility.FromJson<PeerList>("{\"items\":" + json + "}").items ?? new PeerInfo[0]; }
            catch { return new PeerInfo[0]; }
        }

        static readonly Regex CountRow = new Regex("\"([^\"]+)\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        static void Answer(string json)
        {
            CountAnswer a;
            try { a = JsonUtility.FromJson<CountAnswer>(json); }
            catch { return; }
            if (a == null || !counts.TryGetValue(a.id, out var done)) return;
            counts.Remove(a.id);
            var result = new Dictionary<string, int>();
            int at = json.IndexOf("\"rooms\"", StringComparison.Ordinal);
            if (a.ok && at >= 0)
            {
                int open = json.IndexOf('{', at), close = open >= 0 ? json.IndexOf('}', open) : -1;
                if (open >= 0 && close > open)
                    foreach (Match m in CountRow.Matches(json.Substring(open, close - open + 1)))
                        result[m.Groups[1].Value] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            try { done?.Invoke(a.ok, result); }
            catch (Exception x) { Debug.LogException(x); }
        }

        /// <summary>Receives the page's SendMessage calls and reads the changed poses once a frame.</summary>
        internal class Receiver : MonoBehaviour
        {
            readonly List<KeyValuePair<HawkesNetRoom, Event>> later = new List<KeyValuePair<HawkesNetRoom, Event>>();
            readonly List<string> laterCounts = new List<string>();
            internal void Later(HawkesNetRoom r, Event e) => later.Add(new KeyValuePair<HawkesNetRoom, Event>(r, e));
            internal void LaterCount(int id, string json) => laterCounts.Add(json.Insert(1, "\"id\":" + id + ","));

            // Called from the page: SendMessage('HawkesNet', 'OnNet', json).
            void OnNet(string json)
            {
                Event e;
                try { e = JsonUtility.FromJson<Event>(json); }
                catch (Exception x) { Debug.LogWarning("[HawkesNet] " + x.Message); return; }
                if (e == null || !rooms.TryGetValue(e.h, out var r)) return;
                if (e.e == "error") rooms.Remove(e.h);
                try { r.Dispatch(e); }
                catch (Exception x) { Debug.LogException(x); }
            }

            void OnApplicationQuit()
            {
                foreach (var c in managed.Values) c.Leave();
                managed.Clear();
            }

            // Called from the page: SendMessage('HawkesNet', 'OnNetCount', json).
            void OnNetCount(string json) => Answer(json);

            void Update()
            {
                if (later.Count > 0)
                {
                    var now = later.ToArray();
                    later.Clear();
                    foreach (var kv in now)
                    {
                        if (kv.Value.e == "error") rooms.Remove(kv.Key.handle);
                        try { kv.Key.Dispatch(kv.Value); }
                        catch (Exception x) { Debug.LogException(x); }
                    }
                }
                if (laterCounts.Count > 0)
                {
                    var now = laterCounts.ToArray();
                    laterCounts.Clear();
                    foreach (var json in now) Answer(json);
                }
                if (!Live)
                {
                    while (managedCounts.TryDequeue(out var cj)) Answer(cj);
                    foreach (var kv in new List<KeyValuePair<int, HawkesNetClient>>(managed))
                    {
                        while (kv.Value.Events.TryDequeue(out var json)) OnNet(json);
                        if (rooms.TryGetValue(kv.Key, out var mr)) { var mp = kv.Value.TakePoses(); if (mp != null) mr.ReadPoses(mp); }
                    }
                    return;
                }
                foreach (var r in new List<HawkesNetRoom>(rooms.Values))
                {
                    var s = HawkesNet_Poses(r.handle);
                    if (s != null) r.ReadPoses(s);
                }
            }
        }
    }
}
