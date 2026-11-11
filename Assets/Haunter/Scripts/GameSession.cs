using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haunter
{
    public enum SessionState { Ready, Playing, Paused, Caught, Escaped }

    [DisallowMultipleComponent]
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }
        public PlayerController Player;
        public Transform Exit;
        public ListenerCreature Creature;
        public SessionState State { get; private set; } = SessionState.Ready;
        public float ElapsedSeconds { get; private set; }
        public int SoundsMade { get; private set; }

        GUIStyle title, subtitle, label, small, body, button, centered, objective, numeral;
        float lastLoudness;
        float lastNoiseAt = -10f;
        string lastSound = "SILENCE";
        bool stylesReady;
        const float Width = 1600f, Height = 900f;

        void Awake()
        {
            Instance = this;
            State = SessionState.Ready;
            Time.timeScale = 0f;
            SetCursor(false);
        }

        void OnEnable() { NoiseSystem.Heard += OnNoise; }
        void OnDisable() { NoiseSystem.Heard -= OnNoise; }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Time.timeScale = 1f;
                SetCursor(false);
            }
        }

        void Update()
        {
            if (State == SessionState.Playing)
            {
                ElapsedSeconds += Time.deltaTime;
                if (Input.GetKeyDown(KeyCode.Escape)) Pause();
            }
            else if (State == SessionState.Ready && Input.GetKeyDown(KeyCode.Return)) Begin();
            else if (State == SessionState.Paused && Input.GetKeyDown(KeyCode.Escape)) Resume();
            else if ((State == SessionState.Caught || State == SessionState.Escaped) && Input.GetKeyDown(KeyCode.R)) Restart();
        }

        public void Begin()
        {
            if (State != SessionState.Ready) return;
            State = SessionState.Playing;
            Time.timeScale = 1f;
            SetCursor(true);
            if (Player != null) NoiseSystem.Emit(Player.transform.position + Vector3.up * .03f, 1.1f, NoiseKind.Intro, Player.gameObject);
        }

        public void Catch()
        {
            if (State != SessionState.Playing) return;
            State = SessionState.Caught;
            Time.timeScale = 0f;
            SetCursor(false);
        }

        public void Escape()
        {
            if (State != SessionState.Playing) return;
            State = SessionState.Escaped;
            Time.timeScale = 0f;
            SetCursor(false);
        }

        public void Pause()
        {
            if (State != SessionState.Playing) return;
            State = SessionState.Paused;
            Time.timeScale = 0f;
            SetCursor(false);
        }

        public void Resume()
        {
            if (State != SessionState.Paused) return;
            State = SessionState.Playing;
            Time.timeScale = 1f;
            SetCursor(true);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SetCursor(false);
            Scene active = SceneManager.GetActiveScene();
            if (active.buildIndex >= 0) SceneManager.LoadScene(active.buildIndex);
            else if (!string.IsNullOrEmpty(active.path)) SceneManager.LoadScene(active.path);
        }

        void OnApplicationFocus(bool focused)
        {
            if (FindFirstObjectByType<PrototypeSmokeTest>() != null) return;
            if (!focused && State == SessionState.Playing) Pause();
        }

        static void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnNoise(NoiseEvent noise)
        {
            if (State != SessionState.Playing || noise.Kind == NoiseKind.Creature || noise.Kind == NoiseKind.Intro) return;
            SoundsMade++;
            lastNoiseAt = Time.time;
            lastLoudness = noise.Loudness;
            lastSound = noise.Kind == NoiseKind.Cane ? "CANE TAP" : noise.Kind == NoiseKind.Impact ? "OBJECT IMPACT" : "FOOTSTEP";
        }

        void PrepareStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            title = Style(font, 86, FontStyle.Bold);
            subtitle = Style(font, 48, FontStyle.Normal);
            label = Style(font, 15, FontStyle.Bold);
            small = Style(font, 14, FontStyle.Normal);
            body = Style(font, 21, FontStyle.Normal);
            body.wordWrap = true;
            body.normal.textColor = new Color(.68f, .68f, .68f);
            centered = Style(font, 16, FontStyle.Normal);
            centered.alignment = TextAnchor.MiddleCenter;
            objective = Style(font, 17, FontStyle.Bold);
            objective.alignment = TextAnchor.MiddleCenter;
            numeral = Style(font, 38, FontStyle.Normal);
            button = Style(font, 16, FontStyle.Bold);
            button.alignment = TextAnchor.MiddleCenter;
            button.normal.textColor = Color.black;
            button.hover.textColor = Color.black;
            button.active.textColor = Color.black;
            button.normal.background = Texture2D.whiteTexture;
            button.hover.background = Texture2D.whiteTexture;
            button.active.background = Texture2D.whiteTexture;
            button.focused.textColor = Color.black;
            button.focused.background = Texture2D.whiteTexture;
        }

        static GUIStyle Style(Font font, int size, FontStyle weight)
        {
            GUIStyle style = new GUIStyle { font = font, fontSize = size, fontStyle = weight };
            style.normal.textColor = Color.white;
            return style;
        }

        void OnGUI()
        {
            PrepareStyles();
            Matrix4x4 before = GUI.matrix;
            Color colorBefore = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / Width, Screen.height / Height, 1f));
            if (State == SessionState.Playing) DrawHud();
            else if (State == SessionState.Ready) DrawIntro();
            else if (State == SessionState.Paused) DrawPause();
            else DrawOutcome();
            GUI.matrix = before;
            GUI.color = colorBefore;
        }

        void DrawIntro()
        {
            Fill(new Rect(0, 0, Width, Height), 1f, false);
            DrawEchoArtwork(new Vector2(1180, 432));
            Text(new Rect(110, 82, 600, 24), "S O U N D   I S   Y O U R   O N L Y   S I G H T", label, .55f);
            Fill(new Rect(110, 140, 50, 2), .7f);
            GUI.Label(new Rect(103, 210, 850, 112), "HAUNTER", title);
            Text(new Rect(110, 310, 900, 75), "IN THE DARK", subtitle, .9f);
            GUI.Label(new Rect(112, 427, 640, 100), "Every sound shows you the room.\nEvery sound tells it where to look.", body);
            Text(new Rect(112, 535, 640, 28), "It guards the exit. Draw it away, then slip past.", label, .72f);
            if (DrawButton(new Rect(110, 613, 330, 60), "ENTER THE DARK     /     ENTER")) Begin();
            Text(new Rect(110, 724, 650, 25), "WASD   MOVE       MOUSE   LOOK       CTRL   CROUCH", small, .65f);
            Text(new Rect(110, 753, 700, 25), "SPACE / RMB   TAP CANE       E   PICK UP       HOLD LMB   THROW", small, .65f);
            Text(new Rect(110, 782, 700, 25), "Q   DROP       SHIFT   RUN       ESC   PAUSE", small, .5f);
            Fill(new Rect(110, 847, 1380, 1), .15f);
            Text(new Rect(110, 863, 700, 20), "A FICTIONAL ECHOLOCATION HORROR CONCEPT", small, .4f);
            Text(new Rect(1320, 863, 250, 20), "HEADPHONES ADVISED", small, .4f);
        }

        void DrawHud()
        {
            // Restrained black panels keep the objective readable over very bright overlapping waves.
            Fill(new Rect(594, 17, 412, 120), .7f, false);
            Text(new Rect(48, 40, 330, 24), "H A U N T E R  /  I N  T H E  D A R K", small, .48f);
            GUI.Label(new Rect(610, 26, 380, 28), "FIND THE EXIT", objective);
            if (Player != null && Exit != null)
            {
                Vector3 toExit = Exit.position - Player.transform.position;
                toExit.y = 0;
                Vector3 forward = Player.transform.forward;
                forward.y = 0;
                float angle = Vector3.SignedAngle(forward, toExit, Vector3.up);
                DrawArrow(new Vector2(800, 86), angle, .9f);
                Text(new Rect(850, 74, 130, 28), toExit.magnitude.ToString("0.0") + " M", label, .7f);
                Text(new Rect(623, 77, 120, 22), "EXIT BEARING", small, .4f);
            }
            DrawCrosshair(new Vector2(800, 450));
            if (Player != null)
            {
                string prompt = string.Empty;
                if (Player.HeldObject != null) prompt = "HOLD LMB  THROW " + Player.HeldObject.DisplayName.ToUpperInvariant() + "     /     Q  DROP";
                else if (Player.NearbyObject != null) prompt = "E  PICK UP " + Player.NearbyObject.DisplayName.ToUpperInvariant();
                else if (Exit != null && PlayerController.HorizontalDistance(Player.transform.position, Exit.position) < 2.2f) prompt = "E  LEAVE THE ARCHIVE";
                if (!string.IsNullOrEmpty(prompt))
                {
                    Fill(new Rect(500, 526, 600, 45), .72f, false);
                    GUI.Label(new Rect(500, 526, 600, 45), prompt, centered);
                }
                if (Player.ChargingThrow)
                {
                    Fill(new Rect(700, 590, 200, 2), .2f);
                    Fill(new Rect(700, 590, 200 * Player.ThrowCharge, 2), .95f);
                    Text(new Rect(700, 605, 200, 25), "RELEASE TO THROW", centered, .65f);
                }
                Fill(new Rect(50, 806, 240, 50), .65f, false);
                Text(new Rect(63, 815, 250, 22), "SPACE / RMB   TAP CANE", label, .8f);
                Fill(new Rect(63, 846, 202, 2), .18f);
                Fill(new Rect(63, 846, 202 * Player.CaneReady, 2), .65f);
                string movement = Player.IsCrouching ? "CROUCHING / QUIET" : Player.IsSprinting ? "RUNNING / LOUD" : "CTRL  CROUCH";
                Text(new Rect(50, 873, 320, 20), movement, small, .45f);
            }
            float soundFade = Mathf.Clamp01(1f - (Time.time - lastNoiseAt) / 2.5f);
            Text(new Rect(1276, 815, 270, 24), soundFade > .02f ? lastSound : "SILENCE", label, .5f + soundFade * .35f);
            for (int i = 0; i < 18; i++)
            {
                float height = 5f + Mathf.Sin((i + 1) * 2.1f) * Mathf.Sin((i + 1) * 2.1f) * 24f;
                float alpha = i < lastLoudness / 1.5f * 18 ? .12f + soundFade * .6f : .1f;
                Fill(new Rect(1277 + i * 14, 866 - height, 6, height), alpha);
            }
            if (ElapsedSeconds < 12)
                Text(new Rect(480, 830, 640, 35), "Throw to distract it. It will linger there. Crouch past.", centered, Mathf.Clamp01((12f - ElapsedSeconds) / 2f) * .65f);
        }

        void DrawPause()
        {
            DrawHud();
            Fill(new Rect(0, 0, Width, Height), 1f, false);
            Text(new Rect(110, 160, 650, 30), "TAKE A BREATH", label, .5f);
            GUI.Label(new Rect(103, 235, 900, 115), "PAUSED", title);
            GUI.Label(new Rect(112, 377, 700, 60), "The dark can wait.", body);
            if (DrawButton(new Rect(110, 490, 330, 60), "RESUME     /     ESC")) Resume();
            if (DrawButton(new Rect(110, 575, 330, 60), "START AGAIN")) Restart();
            DrawEchoArtwork(new Vector2(1180, 460));
            Text(new Rect(110, 775, 900, 30), "SPACE / RMB  CANE     E  PICK UP     HOLD LMB  THROW     Q  DROP", small, .55f);
            Text(new Rect(110, 811, 900, 30), "WASD  MOVE     CTRL  CROUCH     SHIFT  RUN     MOUSE  LOOK", small, .55f);
        }

        void DrawOutcome()
        {
            bool escaped = State == SessionState.Escaped;
            Fill(new Rect(0, 0, Width, Height), 1f, false);
            Text(new Rect(110, 140, 800, 30), escaped ? "THE ARCHIVE FALLS SILENT" : "SOMETHING HEARD YOU", label, .5f);
            GUI.Label(new Rect(103, 245, 1250, 115), escaped ? "YOU FOUND THE EXIT." : "IT FOUND YOU.", title);
            GUI.Label(new Rect(112, 398, 850, 100), escaped
                ? "One last step. Then the world opens up."
                : "Let the next sound come from somewhere else.\nCrouch to stay quiet. Throw objects to lead it away.", body);
            Fill(new Rect(110, 538, 800, 1), .2f);
            GUI.Label(new Rect(110, 570, 220, 60), FormatTime(ElapsedSeconds), numeral);
            Text(new Rect(110, 630, 220, 22), "TIME IN THE DARK", small, .5f);
            GUI.Label(new Rect(420, 570, 220, 60), SoundsMade.ToString("00"), numeral);
            Text(new Rect(420, 630, 250, 22), "SOUNDS LEFT BEHIND", small, .5f);
            if (DrawButton(new Rect(110, 727, 330, 60), "TRY AGAIN     /     R")) Restart();
            Text(new Rect(110, 856, 900, 22), "HAUNTER IN THE DARK     /     CONCEPT 01", small, .4f);
        }

        static string FormatTime(float seconds)
        {
            return ((int)seconds / 60).ToString("00") + ":" + ((int)seconds % 60).ToString("00");
        }

        bool DrawButton(Rect rect, string text)
        {
            Fill(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), .65f);
            return GUI.Button(rect, text, button);
        }

        static void Fill(Rect rect, float alpha, bool white = true)
        {
            Color old = GUI.color;
            GUI.color = new Color(white ? 1 : 0, white ? 1 : 0, white ? 1 : 0, alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Text(Rect rect, string value, GUIStyle style, float alpha)
        {
            Color old = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.Label(rect, value, style);
            GUI.color = old;
        }

        static void Line(Vector2 a, Vector2 b, float alpha, float thickness = 1f)
        {
            Matrix4x4 old = GUI.matrix;
            Vector2 delta = b - a;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
            Fill(new Rect(0, -thickness * .5f, delta.magnitude, thickness), alpha);
            GUI.matrix = old;
        }

        static void DrawArrow(Vector2 origin, float angle, float alpha)
        {
            float sin = Mathf.Sin(angle * Mathf.Deg2Rad);
            float cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            Vector2 tail = origin + new Vector2(-20 * sin, 20 * cos);
            Vector2 tip = origin + new Vector2(19 * sin, -19 * cos);
            Vector2 left = origin + new Vector2(-10 * cos + 7 * sin, -10 * sin - 7 * cos);
            Vector2 right = origin + new Vector2(10 * cos + 7 * sin, 10 * sin - 7 * cos);
            Line(tail, tip, alpha, 2);
            Line(tip, left, alpha, 2);
            Line(tip, right, alpha, 2);
        }

        void DrawCrosshair(Vector2 center)
        {
            bool hasObject = Player != null && Player.NearbyObject != null;
            float gap = hasObject ? 9f : 5f;
            float alpha = hasObject ? .95f : .4f;
            Line(center + new Vector2(-gap - 4, 0), center + new Vector2(-gap, 0), alpha);
            Line(center + new Vector2(gap, 0), center + new Vector2(gap + 4, 0), alpha);
            Line(center + new Vector2(0, -gap - 4), center + new Vector2(0, -gap), alpha);
            Line(center + new Vector2(0, gap), center + new Vector2(0, gap + 4), alpha);
            Fill(new Rect(center.x - 1, center.y - 1, 2, 2), alpha);
        }

        static void DrawEchoArtwork(Vector2 center)
        {
            for (int ring = 0; ring < 7; ring++)
            {
                float radius = 53 + ring * 35;
                Vector2 last = center + new Vector2(radius, 0);
                for (int segment = 1; segment <= 96; segment++)
                {
                    float angle = segment / 96f * Mathf.PI * 2;
                    float jitter = Mathf.Sin(angle * 7 + ring) * (1.2f + ring * .6f);
                    Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + jitter);
                    float visibility = .10f + .25f * Mathf.Pow(Mathf.Abs(Mathf.Sin(angle * .5f + ring * .3f)), 3);
                    Line(last, next, visibility, ring == 3 ? 1.7f : 1f);
                    last = next;
                }
            }
            Line(center + new Vector2(-8, 0), center + new Vector2(8, 0), .55f);
            Line(center + new Vector2(0, -8), center + new Vector2(0, 8), .55f);
            Fill(new Rect(center.x - 1, center.y - 1, 2, 2), 1);
        }
    }
}
