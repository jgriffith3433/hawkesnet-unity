using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// Drop-in avatars for a shared room (Hawkes Gen docs/net.md, "Unity: the standard layer"). Put it on any object, set
    /// Project, Area, LiveHosts, Local (your player) and RemotePrefab: it joins the area's room through a HawkesNetLobby,
    /// sends Local's pose, and keeps one RemotePrefab per other player, named after the player and moved every frame by a
    /// HawkesNetTrack: drawn a little behind their own clock, between the two poses around that moment (position and
    /// facing), the delay learned from how late and how evenly their poses arrive, carried on along their velocity and
    /// turn when a pose is late, and the guess's error blended out when it comes. Games with their own avatars use
    /// HawkesNetLobby (or the room's OnPose) with a HawkesNetTrack per player instead.
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
        [Tooltip("The least delay (ms) remotes are drawn behind their own clock. Each remote's delay is learned on its own (how late and how evenly its poses arrive) and is used when it is more; 0 for the learned one only.")]
        public float Delay = 0f;
        [Tooltip("How far (ms) a remote whose poses are late is carried on along its velocity and turn before it waits.")]
        public float MaxExtrapolation = 250f;
        [Tooltip("Join when enabled. Off: call Lobby.Join yourself.")]
        public bool JoinOnEnable = true;

        /// <summary>Small values sent with your pose (a JSON object or a JsonUtility object; null for none).</summary>
        public object Extra;

        public HawkesNetLobby Lobby { get; private set; }
        public readonly Dictionary<string, Remote> Remotes = new Dictionary<string, Remote>();

        /// <summary>One other player: its object, name, newest extra values and the track its poses play from.</summary>
        public sealed class Remote
        {
            public string Id = "", Name = "";
            public GameObject Object;
            public string Extra = "";
            /// <summary>Its poses and how they are played: Delay (ms behind), Ahead (ms carried on past the newest), Jitter...</summary>
            public readonly HawkesNetTrack Track = new HawkesNetTrack();
            /// <summary>Where it was drawn this frame, which way it faces (yaw, degrees) and how fast it moves (m/s).</summary>
            public Vector3 Position, Velocity;
            public float Yaw;
        }

        void Awake()
        {
            Lobby = new HawkesNetLobby(new HawkesNetOptions { project = Project, name = PlayerName }, LiveHosts);
            Lobby.PeerJoined += (id, name) => Ensure(id, name);
            Lobby.PeerLeft += (id, _) => Drop(id);
            Lobby.Left += _ => { foreach (var id in new List<string>(Remotes.Keys)) Drop(id); };
            Lobby.PoseChanged += (id, pose) =>
            {
                var room = Lobby.Room;
                var r = Ensure(id, room != null && room.Peers.TryGetValue(id, out var n) ? n : "");
                r.Extra = pose.extra;
                r.Track.Push(pose, room != null ? room.Now : pose.at);
            };
        }

        void OnEnable() { if (JoinOnEnable && Area.Length > 0) Lobby.Join(Area); }
        void OnDisable() { Lobby.Leave(); }

        void Update()
        {
            var room = Lobby.Room;
            if (room == null || !room.Ready) return;
            if (Local) room.Pose(Local.position, Local.eulerAngles.y, Extra);
            double now = room.Now;
            foreach (var r in Remotes.Values) Play(r, now);
            if (HawkesNet.Watched(room)) Tell(room);
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

        void Play(Remote r, double now)
        {
            r.Track.MinDelay = Delay;
            r.Track.MaxAhead = MaxExtrapolation;
            var was = r.Position;
            if (!r.Track.Sample(now, out var position, out var yaw)) return;
            float dt = Time.unscaledDeltaTime;
            r.Velocity = dt > 0f && r.Object && r.Object.activeSelf ? (position - was) / dt : Vector3.zero;
            r.Position = position; r.Yaw = yaw;
            if (!r.Object) return;
            if (!r.Object.activeSelf) r.Object.SetActive(true);
            r.Object.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        // A tool in the page (Hawkes Studio's playtest bot) asked what is drawn: this frame's remotes, for it to check that
        // they move every frame and never jump. Nothing is built when nobody asks.
        readonly StringBuilder told = new StringBuilder();
        void Tell(HawkesNetRoom room)
        {
            var b = told.Clear();
            var inv = CultureInfo.InvariantCulture;
            b.Append("{\"f\":").Append(Time.frameCount).Append(",\"dt\":").Append((Time.unscaledDeltaTime * 1000f).ToString("0.##", inv)).Append(",\"items\":[");
            bool first = true;
            foreach (var r in Remotes.Values)
            {
                if (r.Track.Count == 0) continue;
                if (!first) b.Append(',');
                first = false;
                b.Append("{\"id\":\"").Append(r.Id).Append("\",\"p\":[")
                    .Append(r.Position.x.ToString("0.####", inv)).Append(',').Append(r.Position.y.ToString("0.####", inv)).Append(',').Append(r.Position.z.ToString("0.####", inv))
                    .Append("],\"y\":").Append(r.Yaw.ToString("0.##", inv))
                    .Append(",\"d\":").Append(r.Track.Delay.ToString("0", inv))
                    .Append(",\"a\":").Append(r.Track.Ahead.ToString("0", inv))
                    .Append(",\"j\":").Append(r.Track.Jitter.ToString("0", inv)).Append('}');
            }
            b.Append("]}");
            HawkesNet.Drawn(room, b.ToString());
        }
    }
}
