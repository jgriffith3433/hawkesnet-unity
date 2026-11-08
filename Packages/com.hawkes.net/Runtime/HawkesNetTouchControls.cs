using System;
using System.Globalization;
using UnityEngine;

namespace Hawkes
{
    /// <summary>
    /// On-screen controls for a phone, in one component and no assets (it draws with OnGUI): a floating joystick on the left
    /// half of the screen (put a thumb down anywhere there and drag) and three emote buttons in a column on the right
    /// (1 wave, 2 hop, 3 spin). They show on a touch device (HawkesNet.IsTouchDevice), when the address has ?touch=1, or from
    /// the first touch anywhere; a mouse and keyboard player never sees them.
    ///
    /// Use: add it next to your game, set StartScreen (so the controls stay quiet while the Join form is up), then each
    /// frame read Move (x right, y up the screen, length 0 to 1: add it to your WASD move) and TakeEmote() (0, or the emote
    /// number tapped since the last call). Touches are read with the legacy Input Manager (Input.touches).
    ///
    /// For a tool in the page (Hawkes Studio's phone playtest) the layout and state go to window.__hawkesNet.touch whenever
    /// they change: where the stick and the buttons are (fractions of the canvas), what Move reads, how many emotes were tapped.
    /// </summary>
    public sealed class HawkesNetTouchControls : MonoBehaviour
    {
        [Tooltip("The start screen: the controls do nothing while it is up (the player is joining).")]
        public HawkesNetStartScreen StartScreen;
        [Tooltip("Show the controls even without a touch screen (also ?touch=1 on the page's address).")]
        public bool ForceShow;
        [Tooltip("How far the thumb must move from where it landed before the stick reads anything (0 to 1 of the stick's radius).")]
        public float Deadzone = 0.15f;
        [Tooltip("How much of the screen's width, from the left, a thumb may land on to steer.")]
        public float StickZone = 0.55f;
        [Tooltip("The words on the three emote buttons.")]
        public string[] EmoteLabels = { "Wave", "Hop", "Spin" };

        /// <summary>True when the controls are shown and read.</summary>
        public bool Active { get; private set; }
        /// <summary>The joystick: x to the right, y up the screen, 0 to 1 long (zero inside the dead zone and while not held).</summary>
        public Vector2 Move { get; private set; }
        /// <summary>A thumb is on the stick.</summary>
        public bool Holding => stickFinger >= 0;
        /// <summary>How many emote buttons have been tapped in all.</summary>
        public int Taps { get; private set; }

        int stickFinger = -1;
        Vector2 origin, stick; // where the thumb landed (screen px, y up) and the stick's pull from there (0 to 1)
        int pendingEmote;
        readonly float[] flash = new float[3];
        bool device, touched;
        string lastReport = "";
        Texture2D disc;
        GUIStyle label;

        // The layout, in screen pixels with y up: rebuilt each frame from the screen size (a phone turns).
        float unit, stickR, buttonR;
        Vector2 restAt;
        readonly Vector2[] buttonAt = new Vector2[3];

        /// <summary>The emote number (1 to 3) tapped since the last call, else 0.</summary>
        public int TakeEmote() { int e = pendingEmote; pendingEmote = 0; return e; }

        void Start()
        {
            device = ForceShow || HawkesNet.IsTouchDevice() || HawkesNetLobby.Query("touch") == "1";
        }

        void Update()
        {
            Layout();
            bool blocked = StartScreen != null && StartScreen.Blocking;
            if (blocked) Release();
#if ENABLE_LEGACY_INPUT_MANAGER
            else
            {
                try { ReadTouches(); }
                catch (InvalidOperationException) { /* the Input System package only: no touches here */ }
            }
#endif
            Active = device || touched;
            Report();
        }

        void Layout()
        {
            float w = Screen.width, h = Screen.height;
            unit = Mathf.Max(0.5f, Mathf.Min(w, h) / 400f);
            stickR = 64f * unit;
            buttonR = 36f * unit;
            float margin = 24f * unit;
            restAt = new Vector2(margin + stickR * 1.3f, margin + stickR * 1.3f);
            for (int i = 0; i < 3; i++)
                buttonAt[i] = new Vector2(w - margin - buttonR, margin + buttonR + i * (buttonR * 2f + 14f * unit));
        }

        // 1 to 3 when the point (screen px, y up) is on an emote button, else 0.
        int ButtonAt(Vector2 p)
        {
            for (int i = 0; i < 3; i++)
                if ((p - buttonAt[i]).sqrMagnitude <= (buttonR * 1.15f) * (buttonR * 1.15f)) return i + 1;
            return 0;
        }

        void Release()
        {
            stickFinger = -1;
            stick = Vector2.zero;
            Move = Vector2.zero;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        void ReadTouches()
        {
            int n = Input.touchCount;
            if (n > 0) touched = true;
            bool seen = false;
            for (int i = 0; i < n; i++)
            {
                Touch t = Input.GetTouch(i);
                Vector2 pos = t.position;
                if (t.phase == TouchPhase.Began)
                {
                    int b = ButtonAt(pos);
                    if (b > 0)
                    {
                        pendingEmote = b; Taps++;
                        flash[b - 1] = Time.unscaledTime;
                    }
                    else if (stickFinger < 0 && pos.x < Screen.width * StickZone)
                    {
                        stickFinger = t.fingerId;
                        origin = pos;
                    }
                }
                if (t.fingerId != stickFinger) continue;
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { stickFinger = -1; stick = Vector2.zero; continue; }
                seen = true;
                Vector2 pull = pos - origin;
                // Past the rim the stick's base follows the thumb, so reversing never needs a long drag back.
                if (pull.magnitude > stickR) { origin = pos - pull.normalized * stickR; pull = pos - origin; }
                stick = Vector2.ClampMagnitude(pull / stickR, 1f);
            }
            if (stickFinger >= 0 && !seen) { stickFinger = -1; stick = Vector2.zero; }
            float mag = stick.magnitude;
            Move = mag < Deadzone ? Vector2.zero : stick.normalized * Mathf.Clamp01((mag - Deadzone) / (1f - Deadzone));
        }
#endif

        // ------------------------------------------------------------------ telling the page

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        void Report()
        {
            if (!HawkesNet.Live) return;
            float w = Screen.width, h = Screen.height;
            var sb = new System.Text.StringBuilder(256);
            sb.Append("{\"active\":").Append(Active ? "true" : "false");
            sb.Append(",\"holding\":").Append(Holding ? "true" : "false");
            sb.Append(",\"move\":[").Append(F(Mathf.Round(Move.x * 10f) / 10f)).Append(',').Append(F(Mathf.Round(Move.y * 10f) / 10f)).Append(']');
            sb.Append(",\"taps\":").Append(Taps);
            sb.Append(",\"stick\":{\"x\":").Append(F(restAt.x / w)).Append(",\"y\":").Append(F(1f - restAt.y / h)).Append(",\"r\":").Append(F(stickR / h));
            sb.Append(",\"zone\":").Append(F(StickZone)).Append('}');
            sb.Append(",\"emotes\":[");
            string[] names = { "wave", "hop", "spin" };
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":").Append(i + 1).Append(",\"name\":\"").Append(names[i]).Append("\",\"x\":").Append(F(buttonAt[i].x / w)).Append(",\"y\":").Append(F(1f - buttonAt[i].y / h)).Append(",\"r\":").Append(F(buttonR / h)).Append('}');
            }
            sb.Append("],\"w\":").Append((int)w).Append(",\"h\":").Append((int)h).Append('}');
            string json = sb.ToString();
            if (json == lastReport) return;
            lastReport = json;
            HawkesNet.ShowedTouch(json);
        }

        // ------------------------------------------------------------------ drawing

        void MakeDisc()
        {
            if (disc != null) return;
            const int size = 96;
            disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    byte a = (byte)(255f * Mathf.Clamp01((1f - d) * c * 0.5f));
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            disc.SetPixels32(px);
            disc.Apply();
        }

        void Disc(Vector2 centre, float radius, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(centre.x - radius, Screen.height - centre.y - radius, radius * 2f, radius * 2f), disc);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (!Active || Event.current.type != EventType.Repaint) return;
            if (StartScreen != null && StartScreen.Blocking) return;
            MakeDisc();
            if (label == null)
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = Color.white } };
            label.fontSize = Mathf.RoundToInt(15f * unit);

            // The stick: a ring where the thumb landed (or at rest, bottom left) and a knob that follows the thumb.
            Vector2 baseAt = Holding ? origin : restAt;
            Disc(baseAt, stickR, new Color(1f, 1f, 1f, Holding ? 0.2f : 0.12f));
            Disc(baseAt + stick * stickR, stickR * 0.45f, new Color(1f, 1f, 1f, Holding ? 0.55f : 0.3f));

            for (int i = 0; i < 3; i++)
            {
                bool lit = Time.unscaledTime - flash[i] < 0.25f;
                Disc(buttonAt[i], buttonR, lit ? new Color(0.62f, 0.91f, 0.44f, 0.85f) : new Color(0.06f, 0.07f, 0.1f, 0.6f));
                string text = i < EmoteLabels.Length ? EmoteLabels[i] : (i + 1).ToString();
                GUI.Label(new Rect(buttonAt[i].x - buttonR, Screen.height - buttonAt[i].y - buttonR, buttonR * 2f, buttonR * 2f), text, label);
            }
        }

        void OnDestroy()
        {
            if (disc != null) Destroy(disc);
        }
    }
}
