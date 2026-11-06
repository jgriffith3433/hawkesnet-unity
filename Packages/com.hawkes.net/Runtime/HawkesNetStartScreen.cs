using System;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// A start screen for a Hawkes Net game, in one component and no assets (it draws with OnGUI): a name, a room, a Join
    /// button, and one line that says where the connection is: "Connecting...", "Online" or "Failed: <reason>", with the
    /// seconds to the next try and a Try again button. Once online it shrinks to a status bar with a Leave button (Esc), so
    /// a player can change the name or the room and join again. The name is remembered (PlayerPrefs) and can come from the
    /// page's address (?name=Ada), so a link can name its player.
    ///
    /// First run: with no Project (and no ticketUrl) set it says what to do instead of failing: make a free project at
    /// net.hawkesgen.ai, put its id in Project, list your site there.
    ///
    /// Use: set Avatars (HawkesNetAvatars with JoinOnEnable off) or call Attach(lobby, area) with your own HawkesNetLobby,
    /// and keep your game from reading the keys while Blocking is true (the player is typing a name). The status line is
    /// also written to the log ("[HawkesNetStart] ...", the browser console on WebGL) each time it changes.
    /// </summary>
    public sealed class HawkesNetStartScreen : MonoBehaviour
    {
        [Tooltip("The avatars component whose lobby this joins (set its Join On Enable off). Or leave empty and call Attach.")]
        public HawkesNetAvatars Avatars;
        [Tooltip("The room's area: everyone who enters the same one plays together.")]
        public string Area = "lobby";
        [Tooltip("Heading on the start screen.")]
        public string Title = "Hawkes Net";
        [Tooltip("Let the player type the room. Off: everyone joins Area.")]
        public bool AllowRoomEntry = true;
        [Tooltip("Draw the status bar (status, room, Leave) once online.")]
        public bool ShowHud = true;
        [Tooltip("Where the name is remembered between visits (PlayerPrefs key).")]
        public string PrefsKey = "hawkesnet.name";

        public const int MaxName = 24;
        public const int MaxRoom = 32;

        /// <summary>The lobby this joins (null until Avatars has made it or Attach was called).</summary>
        public HawkesNetLobby Lobby => lobby ?? (Avatars != null ? Avatars.Lobby : null);
        /// <summary>True while the start screen is up (name and room being chosen, or the first join in progress): a game
        /// should ignore the keyboard then.</summary>
        public bool Blocking => !entered;
        /// <summary>The name in the field (what the next Join uses).</summary>
        public string PlayerName { get; private set; } = "";
        /// <summary>The room (area) in the field.</summary>
        public string RoomField { get; private set; } = "";
        /// <summary>What is wrong with the fields or the setup, in words ("" when nothing): shown under the Join button.</summary>
        public string Error { get; private set; } = "";

        HawkesNetLobby lobby, hooked;
        bool entered; // joined and online at least once since the last Join: the screen shrinks to the bar
        string lastLogged = "";
        bool focused;
        GUIStyle title, label, field, button, line, small;
        Texture2D white;

        /// <summary>Use your own lobby (and its Options) instead of an Avatars component.</summary>
        public void Attach(HawkesNetLobby own, string area)
        {
            lobby = own;
            if (!string.IsNullOrEmpty(area)) Area = area;
            if (RoomField.Length == 0) RoomField = Area;
        }

        void Awake()
        {
            string q = HawkesNetLobby.Query("name");
            string saved = PlayerPrefs.GetString(PrefsKey, "");
            PlayerName = CleanName(q.Length > 0 ? q : saved);
            if (PlayerName.Length == 0 && Avatars != null) PlayerName = CleanName(Avatars.PlayerName);
            if (PlayerName.Length == 0) PlayerName = "Player " + UnityEngine.Random.Range(100, 999);
            RoomField = Area;
        }

        void Start()
        {
            Debug.Log("[HawkesNetStart] start screen up | name " + PlayerName + ", room " + RoomField);
        }

        void Update()
        {
            var l = Lobby;
            if (l == null || l == hooked) return;
            hooked = l;
            l.StatusChanged += _ => OnStatus();
        }

        void OnStatus()
        {
            var l = Lobby;
            if (l == null) return;
            if (l.Status == "online") entered = true;
            string text = Describe(l);
            if (text == lastLogged) return;
            lastLogged = text;
            Debug.Log("[HawkesNetStart] " + l.Status + " | " + text);
        }

        // ------------------------------------------------------------------ the words

        /// <summary>The status as one line a player can read: "Connecting to room x...", "Online · room x · 2 others",
        /// "Failed: the site is not listed in your project (ticket: ...). Trying again in 8 s."</summary>
        public static string Describe(HawkesNetLobby l)
        {
            if (l == null) return "Not connected";
            string room = l.RoomName.Length > 0 ? l.RoomName : l.Area;
            switch (l.Status)
            {
                case "connecting": return "Connecting to " + room + "...";
                case "online":
                    int others = l.Room != null ? Math.Max(0, l.Room.Peers.Count - (l.Room.Peers.ContainsKey(l.Room.Id) ? 1 : 0)) : 0;
                    return "Online · room " + room + " · " + (others == 1 ? "1 other" : others + " others");
                case "reconnecting": return "Connection lost: reconnecting...";
                case "full": return "Room " + room + " is full." + Wait(l);
                case "offline": return "Failed: " + Reason(l.LastProblem) + "." + Wait(l);
                default: return "Not connected";
            }
        }

        static string Wait(HawkesNetLobby l) => l.RetryIn > 0.5f ? " Trying again in " + Mathf.CeilToInt(l.RetryIn) + " s." : "";

        /// <summary>A problem code and detail ("ticket: ticket refused (403)") in a player's words, with the code kept.</summary>
        public static string Reason(string problem)
        {
            if (string.IsNullOrEmpty(problem)) return "the connection was lost";
            string p = problem.ToLowerInvariant();
            string words;
            if (p.StartsWith("webgl-only")) words = "this build has no connection here (WebGL builds join from the page)";
            else if (p.Contains("403") || p.Contains("not allowed") || p.Contains("origin")) words = "this site is not listed in the project (add it at net.hawkesgen.ai)";
            else if (p.Contains("429") || p.Contains("rate")) words = "too many tries: wait a minute";
            else if (p.StartsWith("ticket") || p.Contains("refused")) words = "the room refused the join";
            else if (p.StartsWith("full") || p.Contains("full")) words = "the room is full";
            else if (p.StartsWith("lost") || p.Contains("relay-lost") || p.Contains("closed")) words = "the connection was lost";
            else if (p.Contains("network") || p.Contains("failed to fetch") || p.Contains("timeout") || p.Contains("timed out")) words = "cannot reach Hawkes Net (check the connection)";
            else words = "the room could not be joined";
            return words + " (" + problem + ")";
        }

        /// <summary>A name made fit to send: no control characters, spaces trimmed and collapsed, at most MaxName.</summary>
        public static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder();
            bool space = false;
            foreach (char c in raw)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
                if (sb.Length >= MaxName) break;
            }
            return sb.ToString();
        }

        /// <summary>True for a room (area) name the service takes: letters, digits, - and _ , 1 to MaxRoom long.</summary>
        public static bool ValidRoom(string room)
        {
            if (string.IsNullOrEmpty(room) || room.Length > MaxRoom) return false;
            foreach (char c in room)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }

        // ------------------------------------------------------------------ joining

        /// <summary>Join with the name and room in the fields (what the Join button and Enter do). Returns false, with
        /// Error set, when the fields or the setup are not right.</summary>
        public bool Join()
        {
            var l = Lobby;
            if (l == null) { Error = "Nothing to join with: set Avatars or call Attach."; return false; }
            string name = CleanName(PlayerName), room = RoomField.Trim();
            if (!AllowRoomEntry) room = Area;
            if (name.Length == 0) { Error = "Type a name first."; return false; }
            if (!ValidRoom(room)) { Error = "A room name uses letters, numbers, - and _ (up to " + MaxRoom + ")."; return false; }
            if (SetupMissing(l)) { Error = "Not set up yet: see the steps above."; return false; }
            Error = "";
            PlayerName = name; RoomField = room;
            PlayerPrefs.SetString(PrefsKey, name);
            PlayerPrefs.Save();
            l.Options.name = name;
            if (Avatars != null) Avatars.PlayerName = name;
            entered = false;
            l.Leave();
            l.Join(room);
            return true;
        }

        /// <summary>Back to the start screen: leave the room, keep the fields.</summary>
        public void Leave()
        {
            var l = Lobby;
            if (l != null) l.Leave();
            entered = false; Error = "";
        }

        static bool SetupMissing(HawkesNetLobby l) => string.IsNullOrEmpty(l.Options.project) && string.IsNullOrEmpty(l.Options.ticketUrl) && string.IsNullOrEmpty(l.Options.key);

        // ------------------------------------------------------------------ drawing

        void MakeStyles()
        {
            if (title != null) return;
            white = Texture2D.whiteTexture;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.88f, 0.92f) } };
            title = new GUIStyle(label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            line = new GUIStyle(label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft };
            small = new GUIStyle(label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = new Color(0.62f, 0.67f, 0.74f) } };
        }

        static Color StatusColor(string status)
        {
            switch (status)
            {
                case "online": return new Color(0.62f, 0.91f, 0.44f);
                case "connecting": case "reconnecting": return new Color(1f, 0.85f, 0.3f);
                case "offline": case "full": return new Color(1f, 0.48f, 0.45f);
                default: return new Color(0.7f, 0.74f, 0.8f);
            }
        }

        void Box(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }

        void OnGUI()
        {
            MakeStyles();
            var l = Lobby;
            float s = Mathf.Max(1f, Screen.height / 700f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float w = Screen.width / s, h = Screen.height / s;
            if (!entered) Panel(l, w, h); else if (ShowHud) Hud(l, w);
            GUI.matrix = old;
        }

        void Panel(HawkesNetLobby l, float w, float h)
        {
            bool setup = l != null && SetupMissing(l);
            bool failed = l != null && (l.Status == "offline" || l.Status == "full");
            float pw = 400f, ph = setup ? 430f : (failed ? 340f : 310f);
            var r = new Rect((w - pw) * 0.5f, Mathf.Max(10f, (h - ph) * 0.4f), pw, ph);
            Box(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 1f, 1f, 0.12f));
            Box(r, new Color(0.06f, 0.07f, 0.1f, 0.94f));
            float x = r.x + 20f, y = r.y + 16f, iw = r.width - 40f;

            GUI.Label(new Rect(x, y, iw, 34f), Title, title); y += 40f;
            if (setup)
            {
                GUI.Label(new Rect(x, y, iw, 112f),
                    "First run: this scene has no Hawkes Net project yet.\n1. Make a free project at net.hawkesgen.ai.\n2. Put its id (usr_...) in Project on the Avatars component, or set ticketUrl.\n3. List your site there (the Editor and localhost use test rooms).", line);
                y += 118f;
            }

            if (!focused && Event.current.type == EventType.Repaint) { GUI.FocusControl("hn-name"); focused = true; }
            bool busy = l != null && (l.Status == "connecting" || l.Status == "reconnecting");
            bool editable = !busy;
            GUI.enabled = editable;
            GUI.Label(new Rect(x, y, 70f, 28f), "Name", label);
            GUI.SetNextControlName("hn-name");
            PlayerName = GUI.TextField(new Rect(x + 70f, y, iw - 70f, 28f), PlayerName, MaxName, field); y += 36f;
            if (AllowRoomEntry)
            {
                GUI.Label(new Rect(x, y, 70f, 28f), "Room", label);
                GUI.SetNextControlName("hn-room");
                RoomField = GUI.TextField(new Rect(x + 70f, y, iw - 70f, 28f), RoomField, MaxRoom, field); y += 36f;
            }
            GUI.enabled = true;

            // Enter joins from either field (or from nowhere): the same as the button.
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (busy)
            {
                if (GUI.Button(new Rect(x, y, iw, 36f), "Cancel", button)) Leave();
            }
            else
            {
                string verb = failed ? "Try again" : "Join";
                if (GUI.Button(new Rect(x, y, iw, 36f), verb, button) || enter) { if (enter) e.Use(); Join(); }
            }
            y += 46f;

            string status = (l != null && l.Status != "off") ? Describe(l) : "Pick a name and press Join.";
            string st = l != null ? l.Status : "off";
            var old = line.normal.textColor;
            line.normal.textColor = Error.Length > 0 ? StatusColor("offline") : StatusColor(st);
            GUI.Label(new Rect(x, y, iw, 62f), Error.Length > 0 ? Error : status, line);
            line.normal.textColor = old;
        }

        void Hud(HawkesNetLobby l, float w)
        {
            string st = l != null ? l.Status : "off";
            string text = "Hawkes Net: " + Describe(l);
            var r = new Rect(10f, 10f, Mathf.Min(560f, w - 150f), 28f);
            Box(r, new Color(0f, 0f, 0f, 0.55f));
            Box(new Rect(r.x + 6f, r.y + 9f, 10f, 10f), StatusColor(st));
            var oldc = label.normal.textColor;
            label.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x + 24f, r.y, r.width - 28f, r.height), text, label);
            label.normal.textColor = oldc;
            if (st == "offline" || st == "full")
                if (GUI.Button(new Rect(r.xMax + 8f, r.y, 96f, 28f), "Try again")) l.RetryNow();
            var e = Event.current;
            bool esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
            if (GUI.Button(new Rect(w - 110f, 10f, 100f, 28f), "Leave (Esc)") || esc) { if (esc) e.Use(); Leave(); }
        }
    }
}
