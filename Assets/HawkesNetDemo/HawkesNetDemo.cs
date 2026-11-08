// The package's Avatars sample (Packages/com.hawkes.net/Samples~/Avatars), as a scene Hawkes Studio builds: Samples~ is
// not imported, so this is a copy with the demo project ("netdemo", test rooms) filled in. HawkesNetDemoBuild makes the
// scene and builds it to WebGL. Keep it in step with the sample when the sample changes.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hawkes.Demo
{
    /// <summary>
    /// The smallest multiplayer scene with some personality: a capsule you walk with WASD (or the arrow keys), a camera
    /// behind it, and a capsule for every other player in the area's shared room, through HawkesNetAvatars. Each player
    /// has a colour and a floating name, and three emotes on the keys 1, 2 and 3 (wave, hop, spin) that the others see.
    /// The colour and the emote ride in the pose's Extra values (HawkesNetAvatars.Extra / Remote.Extra), so any game can
    /// do the same. Put it on an empty GameObject in an empty scene, set Project, build for WebGL and open two tabs.
    /// The others are drawn smoothly whatever the connection (HawkesNetAvatars plays them through a HawkesNetTrack);
    /// add ?lag=150 (and &amp;jitter=30) to the page's address to feel a slow one (HawkesNet.SimulatedLatency).
    /// </summary>
    public sealed class HawkesNetDemo : MonoBehaviour
    {
        [Tooltip("Your account's project (usr_...), from https://net.hawkesgen.ai.")]
        public string Project = "netdemo";
        [Tooltip("Your game's own host names (mygame.com): there the rooms are live, anywhere else test rooms.")]
        public string[] LiveHosts = new string[0];
        public string Area = "unity-demo";
        public float Speed = 5f;
        [Tooltip("How fast the player turns to face where it walks (degrees a second): others see the same turn.")]
        public float TurnSpeed = 720f;
        [Tooltip("Half the side of the square arena (m): the ground and its edge walls are at plus and minus this.")]
        public float ArenaHalf = 12f;
        [Tooltip("Closest two capsules may stand (m): nearer than this the local one is eased away, so they never z-fight.")]
        public float MinGap = 0.6f;
        [Tooltip("How fast the local player is eased apart from another (m a second).")]
        public float SeparateSpeed = 3f;

        // What rides with the pose: the player's colour ("#rrggbb"), the emote last played (0 none, 1 wave, 2 hop,
        // 3 spin) and how many emotes this player has played (so the same emote twice in a row plays twice, and one
        // that was already playing when you arrived does not replay).
        [Serializable]
        class Style { public string c = ""; public int e; public int n; }

        static readonly string[] EmoteNames = { "", "wave", "hop", "spin" };
        static readonly string[] EmoteSays = { "", "Hello!", "Woo!", "Wheee!" };
        static readonly float[] EmoteSeconds = { 0f, 1.4f, 1.0f, 0.9f };
        const float BubbleSeconds = 2.2f;

        // One drawn player (you or another): its body to animate, its colour and the emote being played.
        sealed class View
        {
            public Transform Body;
            public Renderer[] Renderers = new Renderer[0];
            public Color Color = Color.gray;
            public bool HasColor;
            public int Seen = -1; // the emote count last seen (-1: not yet, so the first one seen is only a baseline)
            public int Emote;
            public float Started = -100f;
        }

        Transform player;
        HawkesNetAvatars avatars;
        HawkesNetStartScreen start;
        readonly View me = new View();
        readonly Dictionary<string, View> views = new Dictionary<string, View>();
        readonly Style style = new Style();
        GUIStyle nameStyle, bubbleStyle;

        // CreatePrimitive gives the built-in Default-Material (the Standard shader). Nothing in this scene references
        // that shader, so a WebGL build strips it and every primitive renders as Unity's error magenta. So each object
        // gets a material from a shader that is looked up by name (kept by Always Included Shaders: add the shader your
        // render pipeline uses), with fallbacks.
        static Material MakeMaterial(Color color)
        {
            Shader shader = null;
            foreach (var n in new[] { "Standard", "Universal Render Pipeline/Lit", "Sprites/Default" })
                if (!shader) shader = Shader.Find(n);
            var m = new Material(shader);
            m.color = color;
            return m;
        }

        // A player: an empty root (what HawkesNetAvatars moves) with a capsule body and a darker nose on the front, so a
        // spin shows. The body is what emotes animate, so the camera on the root stays still.
        static Transform MakeAvatar(string name, Color color)
        {
            var root = new GameObject(name).transform;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            // CreatePrimitive adds a collider inside the engine, so a build that strips unused engine code (WebGL) drops
            // the physics module unless managed code names these types ("Can't add component because class
            // 'CapsuleCollider' doesn't exist!"). Naming them here keeps them; the scene moves by transform, so nothing
            // needs the colliders.
            Destroy(body.GetComponent<CapsuleCollider>());
            body.transform.SetParent(root, false);
            body.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color);
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Destroy(nose.GetComponent<BoxCollider>());
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.55f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
            nose.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.12f, 0.12f, 0.15f));
            return root;
        }

        static Color Hue(float h) => Color.HSVToRGB(Mathf.Repeat(h, 1f), 0.65f, 0.95f);
        static float HueOf(Color c) { Color.RGBToHSV(c, out var h, out _, out _); return h; }
        static float HueGap(float a, float b) { float d = Mathf.Abs(a - b); return Mathf.Min(d, 1f - d); }

        void Awake()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            // A Plane is 10 m on a side at scale 1.
            ground.transform.localScale = new Vector3(ArenaHalf * 0.2f, 1f, ArenaHalf * 0.2f);
            Destroy(ground.GetComponent<MeshCollider>());
            ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.35f, 0.55f, 0.35f));

            // You: a random colour (Update moves it off a colour someone else in the room already has).
            float hue = UnityEngine.Random.value;
            player = MakeAvatar("Player", Hue(hue));
            player.position = new Vector3(UnityEngine.Random.Range(-4f, 4f), 1f, UnityEngine.Random.Range(-4f, 4f));
            me.Body = player.GetChild(0);
            me.Renderers = player.GetComponentsInChildren<Renderer>();
            SetColor(Hue(hue));

            // Everyone else: a grey avatar, kept inactive as the template (each gets its own colour when its pose arrives).
            var remote = MakeAvatar("Remote template", Color.gray);
            remote.gameObject.SetActive(false);

            // Edge walls: the player is kept inside them (Update), so the arena is bounded whatever the input.
            var wallMaterial = MakeMaterial(new Color(0.55f, 0.5f, 0.45f));
            const float wallHeight = 2f, wallThickness = 0.5f;
            float wallCentre = ArenaHalf + wallThickness * 0.5f, wallLength = ArenaHalf * 2f + wallThickness * 2f;
            AddWall(new Vector3(0f, wallHeight * 0.5f, wallCentre), new Vector3(wallLength, wallHeight, wallThickness), wallMaterial);
            AddWall(new Vector3(0f, wallHeight * 0.5f, -wallCentre), new Vector3(wallLength, wallHeight, wallThickness), wallMaterial);
            AddWall(new Vector3(wallCentre, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, wallLength), wallMaterial);
            AddWall(new Vector3(-wallCentre, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, wallLength), wallMaterial);

            var cam = Camera.main != null ? Camera.main : new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(player, false);
            cam.transform.localPosition = new Vector3(0f, 4f, -8f);
            cam.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);

            // ?lag=<ms>&jitter=<ms> on the page's address: the others' poses arrive that much later (a slow connection).
            HawkesNet.SimulatedLatency = UrlNumber("lag");
            HawkesNet.SimulatedJitter = UrlNumber("jitter");

            // HawkesNetAvatars joins in OnEnable: switched off while its fields go in, on again to join.
            gameObject.SetActive(false);
            avatars = gameObject.AddComponent<HawkesNetAvatars>();
            avatars.Project = Project;
            avatars.LiveHosts = LiveHosts;
            avatars.Area = Area;
            avatars.PlayerName = "Player " + UnityEngine.Random.Range(100, 999);
            avatars.Local = player;
            avatars.RemotePrefab = remote.gameObject;
            avatars.Extra = style;
            // The start screen joins (name, room, status, errors); Avatars waits for it.
            avatars.JoinOnEnable = false;
            start = gameObject.AddComponent<HawkesNetStartScreen>();
            start.Avatars = avatars;
            start.Area = Area;
            gameObject.SetActive(true);
        }

        // A number from the page's address (?name=value), 0 when it isn't there (and outside a WebGL build).
        static float UrlNumber(string name)
        {
            var url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q < 0) return 0f;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == name && float.TryParse(part.Substring(eq + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return Mathf.Clamp(v, 0f, 2000f);
            }
            return 0f;
        }

        // An edge wall: a visible box with no collider (the scene moves by transform, see Awake).
        static void AddWall(Vector3 centre, Vector3 size, Material material)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Edge wall";
            Destroy(wall.GetComponent<BoxCollider>());
            wall.transform.position = centre;
            wall.transform.localScale = size;
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }

        void SetColor(Color c)
        {
            me.Color = c; me.HasColor = true;
            foreach (var r in me.Renderers) if (r.gameObject.name == "Body") r.material.color = c;
            style.c = "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        void Play(View v, int emote)
        {
            if (emote < 1 || emote >= EmoteNames.Length) return;
            v.Emote = emote; v.Started = Time.time;
        }

        // F3 shows the detail line under the latency (how far behind others are drawn, how far one is predicted).
        bool debug;
        string hudMain = "", hudDetail = "";

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) debug = !debug;
            // While the start screen is up the player is typing a name: no walking, no emotes.
            bool typing = start != null && start.Blocking;
            var move = typing ? Vector3.zero : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 0.01f)
            {
                player.position += move.normalized * Speed * Time.deltaTime;
                // Turn towards where it walks (not at once): the others see the same smooth turn.
                player.rotation = Quaternion.RotateTowards(player.rotation, Quaternion.LookRotation(move), TurnSpeed * Time.deltaTime);
            }
            // Two capsules never share a spot (they would z-fight): within MinGap of another player, ease away from it.
            // Local only: the others do the same on their side, and the push syncs through the pose like any move.
            string selfId = avatars.Lobby != null && avatars.Lobby.Room != null ? avatars.Lobby.Room.Id : "";
            foreach (var other in avatars.Remotes)
            {
                var r = other.Value;
                if (!r.Object || !r.Object.activeSelf) continue;
                var apart = player.position - r.Object.transform.position;
                apart.y = 0f;
                float gap = apart.magnitude;
                if (gap >= MinGap) continue;
                // On the very same spot there is no "away": each side picks one from the room id, opposite for the two.
                var dir = gap > 0.001f ? apart / gap : Vector3.right;
                if (gap <= 0.001f && string.CompareOrdinal(selfId, other.Key) < 0) dir = Vector3.left;
                // Gentle: at least SeparateSpeed, and a share of what is left so walking into someone cannot pass through.
                float deficit = MinGap - gap;
                player.position += dir * Mathf.Min(deficit, Mathf.Max(SeparateSpeed * Time.deltaTime, deficit * Mathf.Clamp01(12f * Time.deltaTime)));
            }
            // The capsule stands on the ground (centre at 1 m, radius 0.5 m) and stays inside the walls, whatever the input.
            float limit = ArenaHalf - 0.5f;
            var p = player.position;
            player.position = new Vector3(Mathf.Clamp(p.x, -limit, limit), 1f, Mathf.Clamp(p.z, -limit, limit));

            // Emotes: 1 wave, 2 hop, 3 spin. Played here at once and sent with the next pose.
            for (int i = 1; i < EmoteNames.Length && !typing; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    style.e = i; style.n++;
                    Play(me, i);
                }
            Animate(me);

            var room = avatars.Lobby != null ? avatars.Lobby.Room : null;
            var seenHues = new List<float>();
            foreach (var kv in avatars.Remotes)
            {
                var r = kv.Value;
                if (!views.TryGetValue(kv.Key, out var v)) views[kv.Key] = v = new View();
                if (r.Object && v.Body == null && r.Object.transform.childCount > 0)
                {
                    v.Body = r.Object.transform.GetChild(0);
                    v.Renderers = r.Object.GetComponentsInChildren<Renderer>(true);
                }
                Style s = null;
                if (r.Extra.Length > 0) { try { s = JsonUtility.FromJson<Style>(r.Extra); } catch (Exception) { /* not ours: no style */ } }
                if (s == null) continue;
                if (s.c.Length > 0 && ColorUtility.TryParseHtmlString(s.c, out var col))
                {
                    if (!v.HasColor || v.Color != col)
                    {
                        v.Color = col; v.HasColor = true;
                        foreach (var rend in v.Renderers) if (rend && rend.gameObject.name == "Body") rend.material.color = col;
                    }
                    seenHues.Add(HueOf(col));
                }
                // A new emote count: play it (the first count seen is only where this player already was).
                if (v.Seen < 0) v.Seen = s.n;
                else if (s.n != v.Seen) { v.Seen = s.n; Play(v, s.e); }
                Animate(v);
            }

            // Two players on nearly one colour: the one whose id sorts last picks the hue farthest from everyone else's.
            if (room != null && room.Ready && me.HasColor)
            {
                bool clash = false;
                foreach (var kv in avatars.Remotes)
                    if (views.TryGetValue(kv.Key, out var v) && v.HasColor && HueGap(HueOf(v.Color), HueOf(me.Color)) < 0.08f && string.CompareOrdinal(room.Id, kv.Key) > 0) clash = true;
                if (clash)
                {
                    float best = HueOf(me.Color), bestGap = -1f;
                    for (int i = 0; i < 24; i++)
                    {
                        float h = i / 24f, gap = 1f;
                        foreach (var o in seenHues) gap = Mathf.Min(gap, HueGap(h, o));
                        if (gap > bestGap) { bestGap = gap; best = h; }
                    }
                    SetColor(Hue(best));
                }
            }
        }

        // The emote's motion on the body (not the root, which the room and the camera move): wave rocks side to side,
        // hop jumps twice, spin turns once around.
        void Animate(View v)
        {
            if (!v.Body) return;
            float t = (Time.time - v.Started) / (v.Emote > 0 ? EmoteSeconds[v.Emote] : 1f);
            Vector3 pos = Vector3.zero; Quaternion rot = Quaternion.identity;
            if (v.Emote > 0 && t >= 0f && t < 1f)
            {
                switch (v.Emote)
                {
                    case 1: rot = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 6f) * 28f * (1f - t)); break;
                    case 2: pos = new Vector3(0f, Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 1.1f, 0f); break;
                    case 3: rot = Quaternion.Euler(0f, Mathf.SmoothStep(0f, 360f, t), 0f); break;
                }
            }
            v.Body.localPosition = pos;
            v.Body.localRotation = rot;
        }

        void OnGUI()
        {
            var lobby = avatars != null ? avatars.Lobby : null;
            if (lobby == null) return;
            if (!start.Blocking)
            {
                GUI.Label(new Rect(10, Screen.height - 28, 640, 24), "Move: WASD / arrows · Emotes: 1 wave · 2 hop · 3 spin");
                // How the others are drawn: how far behind (the delay each one's connection needs), and whether one is
                // being carried on past its newest pose right now.
                if (avatars.Remotes.Count > 0)
                {
                    double most = 0, ahead = 0;
                    foreach (var r in avatars.Remotes.Values) { most = Math.Max(most, r.Track.Delay); ahead = Math.Max(ahead, r.Track.Ahead); }
                    string latency = $"Latency: {most:0} ms", detail = "";
                    GUI.Label(new Rect(10, Screen.height - 50, 640, 24), latency);
                    // F3 toggles the detail: how far behind others are drawn, and how far one is carried on past its newest pose.
                    if (debug)
                    {
                        string lag = HawkesNet.SimulatedLatency > 0f ? $" · simulated lag {HawkesNet.SimulatedLatency:0} ms" + (HawkesNet.SimulatedJitter > 0f ? $" ± {HawkesNet.SimulatedJitter:0}" : "") : "";
                        detail = $"Others drawn {most:0} ms behind{(ahead > 0 ? $" · predicting {ahead:0} ms" : "")}{lag}";
                        GUI.Label(new Rect(10, Screen.height - 72, 640, 24), detail);
                    }
                    // Tell a tool in the page (the playtest bot) what the HUD says, when it changes.
                    if (Event.current.type == EventType.Repaint && (latency != hudMain || detail != hudDetail)) { hudMain = latency; hudDetail = detail; HawkesNet.ShowedHud(latency, detail); }
                }
            }

            if (Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (!cam) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
                bubbleStyle = new GUIStyle(nameStyle) { fontSize = 17 };
            }
            Label(cam, player, avatars.PlayerName + " (you)", me);
            foreach (var kv in avatars.Remotes)
            {
                var r = kv.Value;
                if (!r.Object || !r.Object.activeSelf || !views.TryGetValue(kv.Key, out var v)) continue;
                string name = r.Name;
                if (name.Length == 0 && lobby.Room != null) lobby.Room.Peers.TryGetValue(kv.Key, out name);
                Label(cam, r.Object.transform, string.IsNullOrEmpty(name) ? "Player" : name, v);
            }
        }

        // The name floats above the head in the player's colour, with the emote's words above it for a moment.
        void Label(Camera cam, Transform who, string text, View v)
        {
            var screen = cam.WorldToScreenPoint(who.position + Vector3.up * 1.5f);
            if (screen.z <= 0f) return;
            float x = screen.x, y = Screen.height - screen.y;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(x - 101f, y - 9f, 200f, 24f), text, nameStyle);
            GUI.color = Color.Lerp(Color.white, v.Color, 0.6f);
            GUI.Label(new Rect(x - 100f, y - 10f, 200f, 24f), text, nameStyle);
            float since = Time.time - v.Started;
            if (v.Emote > 0 && since < BubbleSeconds)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((BubbleSeconds - since) * 2f));
                GUI.Label(new Rect(x - 100f, y - 36f - Mathf.Min(since, 0.4f) * 20f, 200f, 26f), EmoteSays[v.Emote] + " (" + EmoteNames[v.Emote] + ")", bubbleStyle);
            }
            GUI.color = old;
        }
    }
}
