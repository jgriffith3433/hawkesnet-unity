using System.Collections.Generic;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Drop-in avatars for a shared room (Hawkes Gen docs/net.md, "Unity: the standard layer"). Put it on any object, set
    /// Project, Area, LiveHosts, Local (your player) and RemotePrefab: it joins the area's room through a HawkesNetLobby,
    /// sends Local's pose, and keeps one RemotePrefab per other player, moved smoothly a little behind their own clock
    /// (Delay), named after the player. Games with their own avatars use HawkesNetLobby (or the room's OnPose) instead.
    /// Extra values (what a player holds, an animation) ride with the pose: set Extra each frame and read
    /// Remote.Extra on the others.
    /// </summary>
    public sealed class HawkesNetAvatars : MonoBehaviour
    {
        [Tooltip("The Hawkes Net project (net/projects.json in Hawkes Gen): its sites may join without a key.")]
        public string Project = "";
        [Tooltip("The shared room's area: everyone in it plays together.")]
        public string Area = "lobby";
        [Tooltip("The game's own domains (e.g. mygame.com, www.mygame.com); anywhere else plays in beta rooms.")]
        public string[] LiveHosts = new string[0];
        [Tooltip("The player's name others see.")]
        public string PlayerName = "";
        public Transform Local;
        public GameObject RemotePrefab;
        [Tooltip("How far behind the newest pose remotes are drawn (ms): enough for one or two sends to arrive.")]
        public float Delay = 120f;
        [Tooltip("Join when enabled. Off: call Lobby.Join yourself.")]
        public bool JoinOnEnable = true;

        /// <summary>Small values sent with your pose (a JSON object or a JsonUtility object; null for none).</summary>
        public object Extra;

        public HawkesNetLobby Lobby { get; private set; }
        public readonly Dictionary<string, Remote> Remotes = new Dictionary<string, Remote>();

        /// <summary>One other player: its object, name, newest extra values and the poses being played.</summary>
        public sealed class Remote
        {
            public string Id = "", Name = "";
            public GameObject Object;
            public string Extra = "";
            internal readonly List<HawkesNetPose> buffer = new List<HawkesNetPose>();
        }

        void Awake()
        {
            Lobby = new HawkesNetLobby(new HawkesNetOptions { project = Project, name = PlayerName }, LiveHosts);
            Lobby.PeerJoined += (id, name) => Ensure(id, name);
            Lobby.PeerLeft += (id, _) => Drop(id);
            Lobby.Left += _ => { foreach (var id in new List<string>(Remotes.Keys)) Drop(id); };
            Lobby.PoseChanged += (id, pose) =>
            {
                var r = Ensure(id, Lobby.Room != null && Lobby.Room.Peers.TryGetValue(id, out var n) ? n : "");
                r.Extra = pose.extra;
                r.buffer.Add(pose);
                if (r.buffer.Count > 32) r.buffer.RemoveAt(0);
            };
        }

        void OnEnable() { if (JoinOnEnable && Area.Length > 0) Lobby.Join(Area); }
        void OnDisable() { Lobby.Leave(); }

        void Update()
        {
            var room = Lobby.Room;
            if (room == null || !room.Ready) return;
            if (Local) room.Pose(Local.position, Local.eulerAngles.y, Extra);
            double at = room.Now - Delay;
            foreach (var r in Remotes.Values) Play(r, at);
        }

        Remote Ensure(string id, string name)
        {
            if (Remotes.TryGetValue(id, out var r)) return r;
            r = new Remote { Id = id, Name = name ?? "" };
            if (RemotePrefab)
            {
                r.Object = Instantiate(RemotePrefab);
                r.Object.name = "Remote " + (r.Name.Length > 0 ? r.Name : id);
                r.Object.SetActive(false); // until its first pose
            }
            Remotes[id] = r;
            return r;
        }

        void Drop(string id)
        {
            if (!Remotes.TryGetValue(id, out var r)) return;
            if (r.Object) Destroy(r.Object);
            Remotes.Remove(id);
        }

        // Between the two poses around the playback time; past the newest, hold it (no guessing ahead).
        static void Play(Remote r, double at)
        {
            var b = r.buffer;
            if (b.Count == 0 || !r.Object) return;
            while (b.Count > 2 && b[1].at <= at) b.RemoveAt(0);
            HawkesNetPose a = b[0], c = b.Count > 1 ? b[1] : b[0];
            float t = c.at > a.at ? Mathf.Clamp01((float)((at - a.at) / (c.at - a.at))) : 1f;
            if (b.Count == 1 || at >= c.at) { a = c; t = 1f; }
            if (!r.Object.activeSelf) r.Object.SetActive(true);
            r.Object.transform.SetPositionAndRotation(Vector3.Lerp(a.position, c.position, t), Quaternion.Euler(0f, Mathf.LerpAngle(a.yaw, c.yaw, t), 0f));
        }
    }
}
