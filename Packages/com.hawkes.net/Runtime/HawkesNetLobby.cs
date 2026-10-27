using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// The standard way a Hawkes game puts its players together (Hawkes Gen docs/net.md, "Unity: the standard layer"): one
    /// shared room per area ("lobby", "level-1"), everyone in it, kept joined. It names the room for where the page runs,
    /// moves to the next shard when a room is full, joins again by itself when the connection is lost or refused, and gives
    /// one status for the HUD. Its events stay subscribed across rejoins and shards, so a game subscribes once.
    ///
    /// Room names (the project's test prefix "t-" keeps beta and playtests apart from live, free and out of the counts):
    ///   live site:  "level-1", then "level-1.2", "level-1.3"... when full
    ///   any other:  "t-level-1"                       (beta sites, localhost)
    ///   playtest:   "t-&lt;id&gt;/level-1"                  (?room=&lt;id&gt; on the page: Gen's playtest.run with peers)
    /// A prefix (a protocol version, "p1/") goes in front of the area: "p1/level-1".
    /// </summary>
    public sealed class HawkesNetLobby
    {
        /// <summary>The page's hosts that count as live (the game's own domains); anything else is beta.</summary>
        public string[] LiveHosts = new string[0];
        /// <summary>Put before every area (e.g. "p1/" for a protocol version, so pages that can't talk never meet).</summary>
        public string Prefix = "";
        /// <summary>The most shards tried before the lobby says "full" and waits.</summary>
        public int MaxShards = 20;
        /// <summary>The join options (project, name, transport...). The name can change before the next join.</summary>
        public readonly HawkesNetOptions Options;

        /// <summary>The room now (null before a join and while between rooms). Prefer the lobby's events to the room's.</summary>
        public HawkesNetRoom Room { get; private set; }
        /// <summary>The area asked for ("" when none), the shard in it (1 = the first) and the room's full name.</summary>
        public string Area { get; private set; } = "";
        public int Shard { get; private set; } = 1;
        public string RoomName { get; private set; } = "";
        /// <summary>"off" (no area asked for), "connecting", "online", "reconnecting" (the SDK is getting the connection
        /// back), "offline" (refused or lost: joining again by itself) or "full" (every shard tried is full: waiting).</summary>
        public string Status { get; private set; } = "off";
        /// <summary>The last problem's code and detail ("ticket", "full", "webgl-only"...), for logs.</summary>
        public string LastProblem { get; private set; } = "";
        public bool Ready => Room != null && Room.Ready;

        public event Action<HawkesNetRoom> Joined;
        /// <summary>The room was left or lost (the lobby joins again unless Leave was called).</summary>
        public event Action<HawkesNetRoom> Left;
        public event Action<string, string> PeerJoined;
        public event Action<string, string> PeerLeft;
        public event Action<string> HostChanged;
        /// <summary>A peer's page went into the background or came back (id, away): show them as away.</summary>
        public event Action<string, bool> PeerAway;
        public event Action<string, string, string> Message;
        public event Action<string, HawkesNetPose> PoseChanged;
        public event Action<string> StatusChanged;

        double retryAt = -1;
        float backoff;
        bool explicitShard;
        static readonly List<HawkesNetLobby> lobbies = new List<HawkesNetLobby>();
        static Ticker ticker;

        public HawkesNetLobby(HawkesNetOptions options, params string[] liveHosts)
        {
            Options = options ?? new HawkesNetOptions();
            LiveHosts = liveHosts ?? new string[0];
        }

        // ------------------------------------------------------------------ where the page runs

        /// <summary>The page's address ("" outside a WebGL build).</summary>
        public static string PageUrl => Application.platform == RuntimePlatform.WebGLPlayer ? Application.absoluteURL ?? "" : "";

        /// <summary>The page's host name, lower case ("" outside a page).</summary>
        public static string PageHost
        {
            get
            {
                try { return PageUrl.Length > 0 ? new Uri(PageUrl).Host.ToLowerInvariant() : ""; }
                catch { return ""; }
            }
        }

        /// <summary>A query value of the page's address ("" when absent).</summary>
        public static string Query(string name)
        {
            string url = PageUrl;
            int q = url.IndexOf('?');
            if (q < 0) return "";
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == name) return Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return "";
        }

        /// <summary>True on one of the game's live hosts.</summary>
        public bool IsLive => Array.IndexOf(LiveHosts, PageHost) >= 0;

        /// <summary>The playtest's private room id (?room= on the page), "" when not a playtest.</summary>
        public static string TestRoom
        {
            get
            {
                string id = Query("room");
                return System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_.-]{1,24}$") ? id : "";
            }
        }

        /// <summary>The full room name for an area and shard here (see the class notes).</summary>
        public string NameFor(string area, int shard = 1)
        {
            string name = Prefix + area + (shard > 1 ? "." + shard : "");
            string test = TestRoom;
            if (test.Length > 0) return "t-" + test + "/" + name;
            return IsLive ? name : "t-" + name;
        }

        // ------------------------------------------------------------------ joining

        /// <summary>Join an area's shared room (the first shard with room, or this shard). Joining the area you are in
        /// does nothing; another one leaves this first.</summary>
        public void Join(string area, int shard = 0)
        {
            if (string.IsNullOrEmpty(area)) { Leave(); return; }
            if (area == Area && (shard == 0 || shard == Shard) && Room != null && Room.Status != "closed") return;
            Close("moved");
            Area = area;
            explicitShard = shard > 0;
            Shard = Math.Max(1, shard);
            backoff = 0f;
            Track(this);
            Open();
        }

        /// <summary>Leave and stay out (no rejoining) until the next Join.</summary>
        public void Leave()
        {
            Area = "";
            retryAt = -1;
            Close("left");
            SetStatus("off");
            lobbies.Remove(this);
        }

        void Open()
        {
            RoomName = NameFor(Area, Shard);
            SetStatus("connecting");
            var room = HawkesNet.Join(RoomName, Options.Copy());
            Room = room;
            room.OnReady += r => { if (r != Room) return; backoff = 0f; LastProblem = ""; SetStatus("online"); Joined?.Invoke(r); };
            room.OnPeerJoin += (id, name) => { if (room == Room) PeerJoined?.Invoke(id, name); };
            room.OnPeerLeave += (id, why) => { if (room == Room) PeerLeft?.Invoke(id, why); };
            room.OnHost += id => { if (room == Room) HostChanged?.Invoke(id); };
            room.OnPeerAway += (id, away) => { if (room == Room) PeerAway?.Invoke(id, away); };
            room.OnMessage += (kind, data, from) => { if (room == Room) Message?.Invoke(kind, data, from); };
            room.OnPose += (id, pose) => { if (room == Room) PoseChanged?.Invoke(id, pose); };
            room.OnStatus += s =>
            {
                if (room != Room) return;
                if (s == "closed") Problem(room, string.IsNullOrEmpty(room.ClosedBecause) ? "lost" : room.ClosedBecause, "");
                else if (room.Ready) SetStatus(s);
            };
            room.OnProblem += (code, detail) => { if (room == Room) Problem(room, code, detail); };
        }

        void Problem(HawkesNetRoom room, string code, string detail)
        {
            detail = detail ?? "";
            LastProblem = code + (detail.Length == 0 ? "" : ": " + detail);
            if (room.Status != "closed") return; // a notice while the room carries on ("relay-lost" is the SDK's to fix)
            bool wasReady = room.Ready;
            Close(code);
            if (Area.Length == 0) return;
            if ((code == "full" || detail.Contains("full")) && !explicitShard && Shard < MaxShards)
            {
                Shard++;
                Debug.Log("[HawkesNet] " + RoomName + " is full: trying shard " + Shard);
                Open();
                return;
            }
            if (code == "webgl-only") { SetStatus("offline"); return; } // nothing to retry: no page SDK here
            // Lost or refused: join again later, backing off (a refused ticket waits longest: the door's limits).
            bool refused = !wasReady && (code == "ticket" || detail.Contains("403") || detail.Contains("429") || detail.Contains("refused"));
            backoff = refused ? Mathf.Min(300f, Mathf.Max(30f, backoff * 2f)) : Mathf.Min(60f, Mathf.Max(2f, backoff * 2f));
            retryAt = Time.realtimeSinceStartup + backoff;
            SetStatus(code == "full" ? "full" : "offline");
            Debug.Log("[HawkesNet] " + RoomName + ": " + LastProblem + ". Joining again in " + backoff.ToString("0") + " s");
        }

        void Close(string why)
        {
            var room = Room;
            Room = null;
            if (room == null) return;
            if (room.Status != "closed") room.Leave();
            Left?.Invoke(room);
        }

        void Tick()
        {
            if (Area.Length == 0 || Room != null || retryAt < 0 || Time.realtimeSinceStartup < retryAt) return;
            retryAt = -1;
            if (!explicitShard) Shard = 1; // the first shard may have room again
            Open();
        }

        void SetStatus(string s)
        {
            if (Status == s) return;
            Status = s;
            StatusChanged?.Invoke(s);
        }

        static void Track(HawkesNetLobby lobby)
        {
            if (!lobbies.Contains(lobby)) lobbies.Add(lobby);
            if (ticker) return;
            var go = new GameObject("HawkesNetLobby");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            ticker = go.AddComponent<Ticker>();
        }

        sealed class Ticker : MonoBehaviour
        {
            void Update()
            {
                for (int i = lobbies.Count - 1; i >= 0; i--)
                {
                    try { lobbies[i].Tick(); }
                    catch (Exception x) { Debug.LogException(x); }
                }
            }
        }
    }
}
