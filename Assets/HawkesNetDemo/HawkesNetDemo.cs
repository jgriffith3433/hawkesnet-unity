// The package's Avatars sample (Packages/com.hawkes.net/Samples~/Avatars), as a scene Hawkes Studio builds: Samples~ is
// not imported, so this is a copy with the demo project ("netdemo", test rooms) filled in. HawkesNetDemoBuild makes the
// scene and builds it to WebGL. Keep it in step with the sample when the sample changes.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Hawkes.Demo
{
    /// <summary>
    /// The smallest multiplayer scene with some personality: a tinted RobotExpressive humanoid you walk with WASD (or the
    /// arrow keys, or a touch joystick on a phone), a camera behind it, and the same figure for every other player in the
    /// area's shared room, through HawkesNetAvatars. Each player has a colour and a floating name, three emotes on the
    /// keys 1, 2 and 3 (wave, hop, spin) and Space to jump. Remotes drive their Animator from HawkesNetTrack's smoothed
    /// velocity (not raw packets); emote and jump counts ride in the pose Extra. A capsule ground probe lands Jump →
    /// Land → Locomotion on slopes and steps. Put it on an empty GameObject in an empty scene, set Project, build for
    /// WebGL and open two tabs. Add ?lag=150 (and &amp;jitter=30) to feel a slow connection.
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
        [Tooltip("Closest two avatars may stand (m): nearer than this the local one is eased away (soft push-apart).")]
        public float MinGap = 0.6f;
        [Tooltip("How fast the local player is eased apart from another (m a second).")]
        public float SeparateSpeed = 3f;

        // What rides with the pose: colour ("#rrggbb"), emote last played (0 none, 1 wave, 2 hop, 3 spin) + count n,
        // and jump count j (so the same jump twice in a row plays twice on every screen).
        [Serializable]
        class Style { public string c = ""; public int e; public int n; public int j; }

        static readonly string[] EmoteNames = { "", "wave", "hop", "spin" };
        static readonly string[] EmoteSays = { "", "Hello!", "Woo!", "Wheee!" };
        static readonly float[] EmoteSeconds = { 0f, 1.9f, 0.8f, 3.4f }; // Wave / Jump / Dance clip lengths
        const float BubbleSeconds = 2.2f;

        sealed class View
        {
            public Transform Body;
            public Animator Animator;
            public Color Color = Color.gray;
            public bool HasColor;
            public int Seen = -1;
            public int SeenJump = -1;
            public int Emote;
            public float Started = -100f;
            public float GroundSpeed;
            public bool Grounded = true;
            public float AirWhileGrounded;
            public float Vy;
            public bool Jumping;
        }

        Transform player;
        HawkesNetAvatars avatars;
        HawkesNetStartScreen start;
        HawkesNetTouchControls touch;
        readonly View me = new View();
        readonly Dictionary<string, View> views = new Dictionary<string, View>();
        readonly Style style = new Style();
        readonly StringBuilder animBuf = new StringBuilder(512);
        GUIStyle nameStyle, bubbleStyle, hudStyle;

        static Material MakeMaterial(Color color)
        {
            Shader shader = null;
            foreach (var n in new[] { "Standard", "Universal Render Pipeline/Lit", "Sprites/Default" })
                if (!shader) shader = Shader.Find(n);
            var m = new Material(shader);
            m.color = color;
            return m;
        }

        static Transform MakeAvatar(string name, Color color) => HawkesNetAvatarVisual.Make(name, color);

        static Color Hue(float h) => Color.HSVToRGB(Mathf.Repeat(h, 1f), 0.65f, 0.95f);
        static float HueOf(Color c) { Color.RGBToHSV(c, out var h, out _, out _); return h; }
        static float HueGap(float a, float b) { float d = Mathf.Abs(a - b); return Mathf.Min(d, 1f - d); }

        void Awake()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(ArenaHalf * 0.2f, 1f, ArenaHalf * 0.2f);
            // Keep the MeshCollider: the capsule ground probe needs it for slopes, steps and landings.
            ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.35f, 0.55f, 0.35f));

            // A low ramp and a step near the walls (away from the usual spawn) so Jump → Land is exercised on slopes.
            AddProp("Ramp", new Vector3(8f, 0.35f, -6f), new Vector3(4f, 0.7f, 2.5f), Quaternion.Euler(0f, 0f, -12f), new Color(0.45f, 0.5f, 0.4f));
            AddProp("Step", new Vector3(-8f, 0.25f, 6f), new Vector3(2.5f, 0.5f, 2.5f), Quaternion.identity, new Color(0.5f, 0.48f, 0.42f));
            // Code-built dressing (no GPU): corner posts and benches so a short walk always hits something to look at.
            var post = new Color(0.42f, 0.38f, 0.34f);
            var seat = new Color(0.48f, 0.4f, 0.32f);
            AddProp("Post NE", new Vector3(10f, 1f, 10f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity, post);
            AddProp("Post NW", new Vector3(-10f, 1f, 10f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity, post);
            AddProp("Post SE", new Vector3(10f, 1f, -10f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity, post);
            AddProp("Post SW", new Vector3(-10f, 1f, -10f), new Vector3(0.4f, 2f, 0.4f), Quaternion.identity, post);
            AddProp("Bench E", new Vector3(9f, 0.35f, 0f), new Vector3(0.5f, 0.4f, 2.2f), Quaternion.identity, seat);
            AddProp("Bench W", new Vector3(-9f, 0.35f, 0f), new Vector3(0.5f, 0.4f, 2.2f), Quaternion.identity, seat);
            AddProp("Banner N", new Vector3(0f, 1.6f, 11.2f), new Vector3(3f, 1.2f, 0.12f), Quaternion.identity, new Color(0.55f, 0.25f, 0.22f));

            float hue = UnityEngine.Random.value;
            player = MakeAvatar("Player", Hue(hue));
            player.position = new Vector3(UnityEngine.Random.Range(-4f, 4f), 0f, UnityEngine.Random.Range(-4f, 4f));
            me.Body = player.childCount > 0 ? player.GetChild(0) : player;
            me.Animator = HawkesNetAvatarVisual.GetAnimator(player);
            SetColor(Hue(hue));

            var remote = MakeAvatar("Remote template", Color.gray);
            remote.gameObject.SetActive(false);

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

            HawkesNet.SimulatedLatency = UrlNumber("lag");
            HawkesNet.SimulatedJitter = UrlNumber("jitter");

            gameObject.SetActive(false);
            avatars = gameObject.AddComponent<HawkesNetAvatars>();
            avatars.Project = Project;
            avatars.LiveHosts = LiveHosts;
            avatars.Area = Area;
            avatars.PlayerName = "Player " + UnityEngine.Random.Range(100, 999);
            avatars.Local = player;
            avatars.RemotePrefab = remote.gameObject;
            avatars.Extra = style;
            avatars.JoinOnEnable = false;
            start = gameObject.AddComponent<HawkesNetStartScreen>();
            start.Avatars = avatars;
            start.Area = Area;
            touch = gameObject.AddComponent<HawkesNetTouchControls>();
            touch.StartScreen = start;
            gameObject.SetActive(true);
        }

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

        static void AddWall(Vector3 centre, Vector3 size, Material material)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Edge wall";
            Destroy(wall.GetComponent<BoxCollider>());
            wall.transform.position = centre;
            wall.transform.localScale = size;
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void AddProp(string name, Vector3 centre, Vector3 size, Quaternion rot, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = centre;
            go.transform.localScale = size;
            go.transform.rotation = rot;
            go.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color);
            // Keep BoxCollider for the capsule ground probe (slopes / steps).
        }

        void SetColor(Color c)
        {
            me.Color = c; me.HasColor = true;
            HawkesNetAvatarVisual.SetTint(player, c);
            style.c = "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        void Play(View v, int emote)
        {
            if (emote < 1 || emote >= EmoteNames.Length) return;
            v.Emote = emote; v.Started = Time.time;
            if (v.Animator) HawkesNetAvatarVisual.PlayEmote(v.Animator, emote);
        }

        void StartJump(View v, bool local)
        {
            v.Jumping = true;
            v.Vy = HawkesNetAvatarVisual.JumpSpeed;
            v.Grounded = false;
            v.AirWhileGrounded = 0f;
            if (v.Animator) HawkesNetAvatarVisual.PlayJump(v.Animator);
            if (local) { style.j++; }
        }

        bool debug;
        string hudMain = "", hudDetail = "";

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) debug = !debug;
            bool typing = start != null && start.Blocking;
            var move = typing ? Vector3.zero : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (!typing && move.sqrMagnitude <= 0.01f && touch != null && touch.Move.sqrMagnitude > 0.0001f) move = new Vector3(touch.Move.x, 0f, touch.Move.y);
            float moveAmt = 0f;
            if (move.sqrMagnitude > 0.01f)
            {
                moveAmt = Speed * Mathf.Min(1f, move.magnitude);
                var delta = move.normalized * moveAmt * Time.deltaTime;
                player.position += new Vector3(delta.x, 0f, delta.z);
                player.rotation = Quaternion.RotateTowards(player.rotation, Quaternion.LookRotation(move), TurnSpeed * Time.deltaTime);
            }

            string selfId = avatars.Lobby != null && avatars.Lobby.Room != null ? avatars.Lobby.Room.Id : "";
            float myRadius = AvatarRadius(player);
            foreach (var other in avatars.Remotes)
            {
                var r = other.Value;
                if (!r.Object || !r.Object.activeSelf) continue;
                var apart = player.position - r.Object.transform.position;
                apart.y = 0f;
                float gap = apart.magnitude;
                float need = Mathf.Max(MinGap, myRadius + AvatarRadius(r.Object.transform));
                if (gap >= need) continue;
                var dir = gap > 0.001f ? apart / gap : Vector3.right;
                if (gap <= 0.001f && string.CompareOrdinal(selfId, other.Key) < 0) dir = Vector3.left;
                float deficit = need - gap;
                player.position += dir * Mathf.Min(deficit, Mathf.Max(SeparateSpeed * Time.deltaTime, deficit * Mathf.Clamp01(12f * Time.deltaTime)));
            }

            // Jump (Space): vertical takeoff; the count rides in Extra so every screen plays Jump → Land.
            if (!typing && me.Grounded && !me.Jumping && Input.GetKeyDown(KeyCode.Space))
                StartJump(me, true);

            IntegrateLocal();

            float limit = ArenaHalf - 0.5f;
            var p = player.position;
            player.position = new Vector3(Mathf.Clamp(p.x, -limit, limit), p.y, Mathf.Clamp(p.z, -limit, limit));

            for (int i = 1; i < EmoteNames.Length && !typing; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    style.e = i; style.n++;
                    Play(me, i);
                }
            int tapped = touch != null ? touch.TakeEmote() : 0;
            if (tapped > 0 && !typing)
            {
                style.e = tapped; style.n++;
                Play(me, tapped);
            }
            me.GroundSpeed = me.Grounded ? moveAmt : 0f;
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
                    v.Animator = HawkesNetAvatarVisual.GetAnimator(r.Object.transform);
                }
                Style s = null;
                if (r.Extra.Length > 0) { try { s = JsonUtility.FromJson<Style>(r.Extra); } catch (Exception) { /* not ours */ } }
                if (s == null) continue;
                if (s.c.Length > 0 && ColorUtility.TryParseHtmlString(s.c, out var col))
                {
                    if (!v.HasColor || v.Color != col)
                    {
                        v.Color = col; v.HasColor = true;
                        HawkesNetAvatarVisual.SetTint(r.Object.transform, col);
                    }
                    seenHues.Add(HueOf(col));
                }
                if (v.Seen < 0) v.Seen = s.n;
                else if (s.n != v.Seen) { v.Seen = s.n; Play(v, s.e); }
                if (v.SeenJump < 0) v.SeenJump = s.j;
                else if (s.j != v.SeenJump)
                {
                    v.SeenJump = s.j;
                    if (v.Animator) HawkesNetAvatarVisual.PlayJump(v.Animator);
                    v.Jumping = true;
                    v.Grounded = false;
                    v.AirWhileGrounded = 0f;
                }
                // Drive remotes from HawkesNetTrack's smoothed velocity (Remote.Velocity), not raw pose packets.
                var vel = r.Velocity; vel.y = 0f;
                v.GroundSpeed = vel.magnitude;
                if (r.Object)
                {
                    // Match local IntegrateLocal: capsule probe + gap to groundY (works on the ramp/step, not only y≈0).
                    bool probe = HawkesNetAvatarVisual.ProbeGround(r.Object.transform, out float groundY, out _);
                    float gap = r.Position.y - groundY;
                    if (v.Jumping && probe && gap <= 0.12f) v.Jumping = false;
                    v.Grounded = probe && gap <= 0.12f && !v.Jumping;
                    if (!v.Grounded && !v.Jumping && r.Position.y <= 0.05f && groundY <= 0.05f)
                        v.Grounded = true; // flat-floor fallback when colliders were stripped
                    if (v.Grounded && HawkesNetAvatarVisual.IsAirborneState(v.Animator))
                        v.AirWhileGrounded += Time.deltaTime;
                    else if (!HawkesNetAvatarVisual.IsAirborneState(v.Animator))
                        v.AirWhileGrounded = 0f;
                }
                Animate(v);
            }

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

            ReportAnim();
        }

        void IntegrateLocal()
        {
            float dt = Time.deltaTime;
            bool probe = HawkesNetAvatarVisual.ProbeGround(player, out float groundY, out _);
            if (me.Jumping || !probe)
            {
                me.Vy -= HawkesNetAvatarVisual.Gravity * dt;
                var p = player.position;
                p.y += me.Vy * dt;
                // Land when the probe hits again on the way down.
                if (me.Vy <= 0f && HawkesNetAvatarVisual.ProbeGround(player, out groundY, out _) && p.y <= groundY + 0.08f)
                {
                    p.y = groundY;
                    me.Vy = 0f;
                    me.Jumping = false;
                    me.Grounded = true;
                    me.AirWhileGrounded = 0f;
                }
                else
                {
                    me.Grounded = false;
                    // Flat-floor safety net if colliders are missing.
                    if (p.y < 0f) { p.y = 0f; me.Vy = 0f; me.Jumping = false; me.Grounded = true; }
                }
                player.position = p;
            }
            else
            {
                me.Vy = 0f;
                me.Grounded = true;
                var p = player.position;
                p.y = groundY;
                player.position = p;
            }
            if (me.Grounded && HawkesNetAvatarVisual.IsAirborneState(me.Animator))
                me.AirWhileGrounded += dt;
            else if (!HawkesNetAvatarVisual.IsAirborneState(me.Animator))
                me.AirWhileGrounded = 0f;
        }

        void ReportAnim()
        {
            var room = avatars != null && avatars.Lobby != null ? avatars.Lobby.Room : null;
            if (room == null || !room.Ready) return;
            var b = animBuf.Clear();
            b.Append("{\"f\":").Append(Time.frameCount).Append(",\"chars\":[");
            float meSlide = HawkesNetAvatarVisual.FootSlideSpeed(me.Animator, me.GroundSpeed, me.Grounded);
            HawkesNetAvatarVisual.AppendAnimSample(b, room.Id ?? "local", me.Grounded, me.GroundSpeed, meSlide, me.AirWhileGrounded, me.Animator, true);
            foreach (var kv in views)
            {
                if (!avatars.Remotes.TryGetValue(kv.Key, out var r) || !r.Object || !r.Object.activeSelf) continue;
                var v = kv.Value;
                float slide = HawkesNetAvatarVisual.FootSlideSpeed(v.Animator, v.GroundSpeed, v.Grounded);
                HawkesNetAvatarVisual.AppendAnimSample(b, kv.Key, v.Grounded, v.GroundSpeed, slide, v.AirWhileGrounded, v.Animator, false);
            }
            b.Append("]}");
            HawkesNet.ShowedAnim(b.ToString());
        }

        static float AvatarRadius(Transform who)
        {
            if (!who) return 0.35f;
            var col = who.GetComponent<CapsuleCollider>();
            return col != null ? Mathf.Max(0.2f, col.radius) : 0.35f;
        }

        void Animate(View v)
        {
            if (v.Animator && v.Animator.runtimeAnimatorController)
            {
                HawkesNetAvatarVisual.Tick(v.Animator, v.GroundSpeed, v.Grounded);
                return;
            }
            if (!v.Body) return;
            float t = (Time.time - v.Started) / (v.Emote > 0 ? EmoteSeconds[v.Emote] : 1f);
            Vector3 pos = Vector3.zero; Quaternion rot = Quaternion.identity;
            if (v.Jumping && !v.Grounded)
                pos = new Vector3(0f, Mathf.Max(0f, player == v.Body.parent ? player.position.y : 0f), 0f);
            else if (v.Emote > 0 && t >= 0f && t < 1f)
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

        float Ui => touch != null && touch.Active ? Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 400f) : 1f;

        void OnGUI()
        {
            var lobby = avatars != null ? avatars.Lobby : null;
            if (lobby == null) return;
            float ui = Ui;
            bool phone = touch != null && touch.Active;
            if (hudStyle == null) hudStyle = new GUIStyle(GUI.skin.label);
            hudStyle.fontSize = phone ? Mathf.RoundToInt(13f * ui) : 0;
            float hx = phone ? Screen.width * 0.42f : 10f, hw = phone ? Screen.width * 0.34f : 640f, lineH = 24f * ui;
            if (!start.Blocking)
            {
                if (!phone) GUI.Label(new Rect(10, Screen.height - 28, 720, 24), "Move: WASD / arrows · Jump: Space · Emotes: 1 wave · 2 hop · 3 spin");
                if (avatars.Remotes.Count > 0)
                {
                    double most = 0, ahead = 0;
                    foreach (var r in avatars.Remotes.Values) { most = Math.Max(most, r.Track.Delay); ahead = Math.Max(ahead, r.Track.Ahead); }
                    string latency = $"Latency: {most:0} ms", detail = "";
                    GUI.Label(new Rect(hx, Screen.height - (phone ? lineH * 1.5f : 50f), hw, lineH), latency, hudStyle);
                    if (debug)
                    {
                        string lag = HawkesNet.SimulatedLatency > 0f ? $" · simulated lag {HawkesNet.SimulatedLatency:0} ms" + (HawkesNet.SimulatedJitter > 0f ? $" ± {HawkesNet.SimulatedJitter:0}" : "") : "";
                        detail = $"Others drawn {most:0} ms behind{(ahead > 0 ? $" · predicting {ahead:0} ms" : "")}{lag}";
                        GUI.Label(new Rect(hx, Screen.height - (phone ? lineH * 3f : 72f), hw, lineH * 1.5f), detail, hudStyle);
                    }
                    if (Event.current.type == EventType.Repaint && (latency != hudMain || detail != hudDetail)) { hudMain = latency; hudDetail = detail; HawkesNet.ShowedHud(latency, detail); }
                }
            }

            if (Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (!cam) return;
            if (nameStyle == null)
            {
                nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                bubbleStyle = new GUIStyle(nameStyle);
            }
            nameStyle.fontSize = Mathf.RoundToInt(15f * ui);
            bubbleStyle.fontSize = Mathf.RoundToInt(17f * ui);
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

        void Label(Camera cam, Transform who, string text, View v)
        {
            var screen = cam.WorldToScreenPoint(who.position + Vector3.up * HawkesNetAvatarVisual.LabelHeight);
            if (screen.z <= 0f) return;
            float x = screen.x, y = Screen.height - screen.y, ui = Ui;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(x - 101f * ui, y - 9f * ui, 200f * ui, 24f * ui), text, nameStyle);
            GUI.color = Color.Lerp(Color.white, v.Color, 0.6f);
            GUI.Label(new Rect(x - 100f * ui, y - 10f * ui, 200f * ui, 24f * ui), text, nameStyle);
            float since = Time.time - v.Started;
            if (v.Emote > 0 && since < BubbleSeconds)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((BubbleSeconds - since) * 2f));
                GUI.Label(new Rect(x - 100f * ui, y - (36f + Mathf.Min(since, 0.4f) * 20f) * ui, 200f * ui, 26f * ui), EmoteSays[v.Emote] + " (" + EmoteNames[v.Emote] + ")", bubbleStyle);
            }
            GUI.color = old;
        }
    }
}
