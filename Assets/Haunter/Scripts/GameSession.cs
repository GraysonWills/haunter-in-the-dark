using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haunter
{
    public enum SessionState { Ready, Playing, Paused, Caught, Escaped, Transitioning }

    [DisallowMultipleComponent]
    public sealed class GameSession : MonoBehaviour
    {
        enum ThrowGuide { Ring, Line, Arc }

        public static GameSession Instance { get; private set; }
        public PlayerController Player;
        public Transform Exit;
        public ListenerCreature Creature;
        public TutorialDirector Tutorial { get; private set; }
        public HaunterCampaign Campaign { get; private set; }
        public int LevelNumber => Campaign ? Campaign.Level : 1;
        public bool ReducedDistortion { get; private set; }
        public SessionState State { get; private set; } = SessionState.Ready;
        public float ElapsedSeconds { get; private set; }
        public int SoundsMade { get; private set; }
        public int CheckpointLevel { get; private set; }
        public int LoadingLevel { get; private set; }
        public int CompletedLevel { get; private set; }

        GUIStyle title, subtitle, label, small, body, button, centered, objective, numeral;
        GUIStyle tutorialHeading, tutorialCopy;
        float lastLoudness;
        float lastNoiseAt = -10f;
        string lastSound = "SILENCE";
        bool stylesReady;
        static bool retryArchiveOnReload;
        bool retryArchiveOnStart;
        ThrowGuide throwGuide;
        const float Width = 1600f, Height = 900f;
        const int ChargeRingSegments = 40;
        const string CheckpointKey = "haunter.checkpoint.v1";
        static readonly Vector2[] ChargeRing = CreateChargeRing();
        static readonly string[] ChapterNames = { "ARCHIVE", "COURTYARD", "BALLROOM", "FINAL MAZE" };

        void Awake()
        {
            Instance = this;
            Tutorial = GetComponent<TutorialDirector>();
            if (Tutorial == null) Tutorial = gameObject.AddComponent<TutorialDirector>();
            Campaign = gameObject.AddComponent<HaunterCampaign>();
            Campaign.Initialize(this);
            ReducedDistortion = PlayerPrefs.GetInt("haunter.reducedDistortion", 0) != 0;
            int checkpoint = PlayerPrefs.GetInt(CheckpointKey, 1);
            CheckpointLevel = checkpoint >= 1 && checkpoint <= ChapterNames.Length ? checkpoint : 1;
            throwGuide = ReadThrowGuide();
            State = SessionState.Ready;
            Time.timeScale = 0f;
            retryArchiveOnStart = retryArchiveOnReload;
            retryArchiveOnReload = false;
            if (!retryArchiveOnStart) SetCursor(false);
        }

        void Start()
        {
            if (!retryArchiveOnStart) return;
            State = SessionState.Playing;
            Time.timeScale = 1f;
            TouchControls.Reset();
            PlayArchive(false);
        }

        void OnEnable() { NoiseSystem.Emitted += OnNoise; }
        void OnDisable() { NoiseSystem.Emitted -= OnNoise; }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Time.timeScale = 1f;
                if (!retryArchiveOnReload) SetCursor(false);
            }
        }

        void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F)) Screen.fullScreen = !Screen.fullScreen;
            if (State == SessionState.Paused && Input.GetKeyDown(KeyCode.Return)) Resume();
#endif
            if (State == SessionState.Playing)
            {
                ElapsedSeconds += Time.deltaTime;
                if (Input.GetKeyDown(KeyCode.Escape) || (TouchControls.Enabled && TouchControls.Pressed(TouchControls.Pause))) Pause();
            }
            else if (State == SessionState.Ready && Input.GetKeyDown(KeyCode.Return)) Begin();
#if !UNITY_WEBGL || UNITY_EDITOR
            else if (State == SessionState.Paused && Input.GetKeyDown(KeyCode.Escape)) Resume();
#endif
            else if (State == SessionState.Caught && Input.GetKeyDown(KeyCode.R)) RetryLevel();
            else if (State == SessionState.Escaped && Input.GetKeyDown(KeyCode.R)) Restart();
        }

        public void Begin(bool skipTutorial = false)
        {
            if (State != SessionState.Ready) return;
            int preview = BrowserBridge.PreviewLevel;
            if (preview >= 2 && preview <= ChapterNames.Length)
            {
                EnterPlay();
                if (Campaign.Enter(preview)) return;
                PlayArchive(skipTutorial);
                return;
            }
            StartLevel(skipTutorial || IsVerificationRun() ? 1 : CheckpointLevel, skipTutorial);
        }

        public void StartLevel(int level, bool skipTutorial = false)
        {
            if (State != SessionState.Ready || level < 1 || level > ChapterNames.Length) return;
            if (level > 1)
            {
                SetCursor(true);
                StartCoroutine(LoadLevel(level, 0));
                return;
            }
            EnterPlay();
            PlayArchive(skipTutorial);
        }

        void EnterPlay()
        {
            State = SessionState.Playing;
            Time.timeScale = 1f;
            SetCursor(true);
        }

        void PlayArchive(bool skipTutorial)
        {
            if (skipTutorial) Tutorial.Skip();
            else Tutorial.StartTutorial();
            if (Player != null) NoiseSystem.Emit(Player.transform.position + Vector3.up * .03f, 1.1f, NoiseKind.Intro, Player.gameObject);
        }

        static bool IsVerificationRun()
        {
            return BrowserBridge.SmokeRequested || BrowserBridge.TutorialSmokeRequested || BrowserBridge.ExpansionSmokeRequested
                || BrowserBridge.CaptureShot > 0 || BrowserBridge.PreviewLevel > 1
                || FindFirstObjectByType<PrototypeSmokeTest>() || FindFirstObjectByType<TutorialSmokeTest>();
        }

        IEnumerator LoadLevel(int level, int completed)
        {
            var previous = State;
            LoadingLevel = level;
            CompletedLevel = completed;
            State = SessionState.Transitioning;
            Time.timeScale = 0f;
            if (Player) Player.CancelThrow();
            TouchControls.Reset();
            float minimumDisplay = completed > 0 ? 1.75f : .6f;
            float shownAt = Time.realtimeSinceStartup;
            yield return null; // Show the transition before synchronously building geometry and navigation.

            if (!Campaign.Enter(level))
            {
                LoadingLevel = CompletedLevel = 0;
                State = previous == SessionState.Playing ? SessionState.Paused : previous;
                SetCursor(false);
                Debug.LogError("Haunter: the next level could not be loaded.");
                yield break;
            }

            if (completed > 0 && level > CheckpointLevel && !IsVerificationRun())
            {
                CheckpointLevel = level;
                PlayerPrefs.SetInt(CheckpointKey, level);
                PlayerPrefs.Save();
            }

            while (Time.realtimeSinceStartup - shownAt < minimumDisplay) yield return null;
            LoadingLevel = CompletedLevel = 0;
            TouchControls.Reset();
            State = SessionState.Playing;
            Time.timeScale = 1f;
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

        public bool PlayerNearExit(float radius)
        {
            return Player && Exit && Mathf.Abs(Player.transform.position.y - Exit.position.y) < 1.05f
                && PlayerController.HorizontalDistance(Player.transform.position, Exit.position) < radius;
        }

        public void ReachExit()
        {
            if (State != SessionState.Playing || !Campaign.CanLeaveCourtyard) return;
            bool legacyTest = LevelNumber == 1 &&
                (FindFirstObjectByType<PrototypeSmokeTest>() || FindFirstObjectByType<TutorialSmokeTest>());
            if (legacyTest || LevelNumber == 4) { Escape(); return; }
            if (BrowserBridge.CaptureShot > 0)
            {
                if (!Campaign.Enter(LevelNumber + 1)) Debug.LogError("Haunter: the next level could not be loaded.");
                return;
            }
            StartCoroutine(LoadLevel(LevelNumber + 1, LevelNumber));
        }

        public void ToggleDistortion()
        {
            ReducedDistortion = !ReducedDistortion;
            PlayerPrefs.SetInt("haunter.reducedDistortion", ReducedDistortion ? 1 : 0);
            PlayerPrefs.Save();
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
            Time.timeScale = Tutorial != null && Tutorial.WorldFrozen ? 0f : 1f;
            SetCursor(true);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SetCursor(false);
            ReloadScene();
        }

        static void ReloadScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.buildIndex >= 0) SceneManager.LoadScene(active.buildIndex);
            else if (!string.IsNullOrEmpty(active.path)) SceneManager.LoadScene(active.path);
        }

        public void RetryLevel()
        {
            if (State != SessionState.Caught && State != SessionState.Paused) return;
            if (LevelNumber == 1)
            {
                retryArchiveOnReload = true;
                SetCursor(true);
                ReloadScene();
                return;
            }
            ElapsedSeconds = 0f;
            SoundsMade = 0;
            SetCursor(true);
            StartCoroutine(LoadLevel(LevelNumber, 0));
        }

        void OnApplicationFocus(bool focused)
        {
            if (BrowserBridge.ExpansionSmokeRequested || FindFirstObjectByType<PrototypeSmokeTest>() != null || FindFirstObjectByType<TutorialSmokeTest>() != null) return;
            // HTML touch buttons can take focus from the canvas without leaving the game.
            // The touch bridge pauses on actual window blur / document visibility loss.
            if (TouchControls.Enabled) return;
            if (!focused && State == SessionState.Playing) Pause();
        }

        static void SetCursor(bool locked)
        {
            TouchControls.Reset();
            if (TouchControls.Enabled) locked = false;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (locked) BrowserBridge.NotifyPointerLockRequest();
#endif
        }

        void OnNoise(NoiseEvent noise)
        {
            if (State != SessionState.Playing || noise.IsReflection || noise.Kind == NoiseKind.Creature || noise.Kind == NoiseKind.Intro) return;
            SoundsMade++;
            lastNoiseAt = Time.time;
            lastLoudness = noise.Loudness;
            lastSound = noise.Kind == NoiseKind.Cane ? "CANE TAP" : noise.Kind == NoiseKind.Impact ? "OBJECT IMPACT"
                : noise.Kind == NoiseKind.Collapse ? "STRUCTURE COLLAPSE" : "FOOTSTEP";
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
            centered.wordWrap = true;
            tutorialHeading = Style(font, 24, FontStyle.Bold);
            tutorialHeading.alignment = TextAnchor.MiddleCenter;
            tutorialCopy = Style(font, 21, FontStyle.Normal);
            tutorialCopy.alignment = TextAnchor.MiddleCenter;
            tutorialCopy.wordWrap = true;
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
            if (State == SessionState.Playing)
            {
                var scan = Campaign.Room ? Campaign.Room.MazeOverview : null;
                if (!HaunterDeveloperView.IsActive && !(scan && scan.Active)) DrawHud();
            }
            else if (State == SessionState.Ready) DrawIntro();
            else if (State == SessionState.Paused) DrawPause();
            else if (State == SessionState.Transitioning) DrawTransition();
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
            if (!TouchControls.Enabled)
            {
                string start = CheckpointLevel > 1 ? "CONTINUE  /  " + ChapterNames[CheckpointLevel - 1] : "ENTER THE DARK     /     ENTER";
                if (DrawButton(new Rect(110, 613, 330, 60), start)) Begin();
                if (DrawButton(new Rect(110, 684, 330, 32), "PLAY WITHOUT TUTORIAL")) Begin(true);
                Text(new Rect(110, 724, 750, 25), "WASD   MOVE       MOUSE   LOOK       " + PlayerController.CrouchKeys + "   CROUCH", small, .65f);
                Text(new Rect(110, 753, 700, 25), "SPACE / RMB   TAP CANE       E   PICK UP       HOLD LMB   THROW", small, .65f);
                Text(new Rect(110, 782, 700, 25), "Q   DROP       " + PlayerController.SprintKeys + "   RUN       ESC   PAUSE", small, .5f);
                Fill(new Rect(1040, 294, 411, 354), .94f, false);
                Text(new Rect(1080, 320, 310, 26), "CHOOSE A LEVEL", label, .72f);
                for (int i = 0; i < ChapterNames.Length; i++)
                    if (DrawButton(new Rect(1080, 365 + i * 62, 330, 48), (i + 1) + "  /  " + ChapterNames[i]))
                        StartLevel(i + 1);
            }
            else Text(new Rect(110, 600, 900, 50), "LEFT STICK   MOVE     DRAG THE VIEW   LOOK", label, .7f);
            Fill(new Rect(110, 847, 1380, 1), .15f);
            Text(new Rect(110, 863, 700, 20), "A FICTIONAL ECHOLOCATION HORROR CONCEPT", small, .4f);
            Text(new Rect(1320, 863, 250, 20), "HEADPHONES ADVISED", small, .4f);
        }

        void DrawTransition()
        {
            // Keep the text and rings proportional even when a phone fills a portrait viewport.
            var matrix = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 620f, Screen.height / 570f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;
            var center = new Vector2(w * .5f, h * .5f);
            Fill(new Rect(0, 0, w, h), 1f, false);
            DrawEchoArtwork(center);
            Fill(new Rect(center.x - 215, center.y - 80, 430, 160), .98f, false);
            string context = CompletedLevel > 0 ? ChapterNames[CompletedLevel - 1] + " CLEARED" : "LEVEL " + LoadingLevel;
            Text(new Rect(center.x - 200, center.y - 60, 400, 26), context, centered, .78f);
            Text(new Rect(center.x - 200, center.y - 21, 400, 42), ChapterNames[LoadingLevel - 1], tutorialHeading, 1f);
            Text(new Rect(center.x - 200, center.y + 36, 400, 26), CompletedLevel > 0 ? "ENTERING NEXT LEVEL" : "ENTERING LEVEL", centered, .62f);
            GUI.matrix = matrix;
        }

        void DrawHud()
        {
            // Restrained black panels keep the objective readable over very bright overlapping waves.
            Fill(new Rect(594, 17, 412, 120), .7f, false);
            Text(new Rect(48, 40, 330, 24), "LEVEL " + LevelNumber + "  /  " + Campaign.Title, small, .65f);
            GUI.Label(new Rect(595, 26, 410, 28), Campaign.Objective, objective);
            if (Player != null && Exit != null)
            {
                Vector3 toExit = Campaign.Bearing - Player.transform.position;
                toExit.y = 0;
                Vector3 forward = Player.transform.forward;
                forward.y = 0;
                float angle = Vector3.SignedAngle(forward, toExit, Vector3.up);
                DrawArrow(new Vector2(800, 86), angle, .9f);
                Text(new Rect(850, 74, 130, 28), toExit.magnitude.ToString("0.0") + " M", label, .7f);
                Text(new Rect(623, 77, 120, 22), LevelNumber == 2 ? "ROUTE BEARING" : "EXIT BEARING", small, .4f);
            }
            if (Player != null && Player.ChargingThrow && Player.ViewCamera != null) DrawThrowFeedback();
            DrawCrosshair(new Vector2(800, 450));
            if (Player != null)
            {
                string prompt = string.Empty;
                if (Player.ChargingThrow)
                    prompt = TouchControls.Enabled ? "RELEASE TO THROW" : "RELEASE LMB TO THROW";
                else if (Player.HeldObject != null) prompt = (TouchControls.Enabled ? "HOLD THROW  /  TAP DROP     " : "HOLD LMB  THROW / Q  DROP     ") + Player.HeldObject.DisplayName.ToUpperInvariant();
                else if (Player.NearbyObject != null) prompt = (TouchControls.Enabled ? "TAP PICK UP     " : "E  PICK UP ") + Player.NearbyObject.DisplayName.ToUpperInvariant();
                else if (PlayerNearExit(2.2f)) prompt = (TouchControls.Enabled ? "TAP EXIT" : "E  CONTINUE") + "  /  " + Campaign.Title;
                if (!string.IsNullOrEmpty(prompt) && (Tutorial == null || !Tutorial.Active))
                {
                    Fill(new Rect(500, 526, 600, 45), .72f, false);
                    GUI.Label(new Rect(500, 526, 600, 45), prompt, centered);
                }
                if (!TouchControls.Enabled && (Tutorial == null || !Tutorial.Active || Tutorial.Stage == TutorialStage.TapCane))
                {
                    Fill(new Rect(50, 806, 240, 50), .65f, false);
                    Text(new Rect(63, 815, 250, 22), "SPACE / RMB   TAP CANE", label, .8f);
                    Fill(new Rect(63, 846, 202, 2), .18f);
                    Fill(new Rect(63, 846, 202 * Player.CaneReady, 2), .65f);
                }
                string movement = Player.IsCrouching ? "CROUCHING / QUIET" : Player.IsSprinting ? "RUNNING / LOUD" : PlayerController.CrouchKeys + "  CROUCH";
                if (!TouchControls.Enabled) Text(new Rect(50, 873, 320, 20), movement, small, .45f);
            }
            float soundFade = Mathf.Clamp01(1f - (Time.time - lastNoiseAt) / 2.5f);
            Text(new Rect(1276, 815, 270, 24), soundFade > .02f ? lastSound : "SILENCE", label, .5f + soundFade * .35f);
            for (int i = 0; i < 18; i++)
            {
                float height = 5f + Mathf.Sin((i + 1) * 2.1f) * Mathf.Sin((i + 1) * 2.1f) * 24f;
                float alpha = i < lastLoudness / 1.5f * 18 ? .12f + soundFade * .6f : .1f;
                Fill(new Rect(1277 + i * 14, 866 - height, 6, height), alpha);
            }
            if (Tutorial != null && Tutorial.Active) DrawTutorial();
            else if (LevelNumber == 1 && ElapsedSeconds < 12)
                Text(new Rect(480, 830, 640, 35), "Throw to distract it. It will linger there. Crouch past.", centered, Mathf.Clamp01((12f - ElapsedSeconds) / 2f) * .65f);
            if (LevelNumber == 3 && NoiseSystem.Instance)
                Text(new Rect(1110, 92, 390, 30), Campaign.Quiet ? "QUIET ALCOVE" :
                    "ECHO OVERLAP  " + Mathf.RoundToInt(NoiseSystem.Instance.AcousticLoad * 100) + "%", small, .7f);
            if (!TouchControls.Enabled && !string.IsNullOrEmpty(Campaign.Hint))
            {
                Fill(new Rect(320, 650, 960, 106), .86f, false);
                GUI.Label(new Rect(345, 665, 910, 30), Campaign.HintHeading, tutorialHeading);
                Text(new Rect(345, 700, 910, 43), Campaign.Hint, tutorialCopy, .9f);
            }
        }

        static ThrowGuide ReadThrowGuide()
        {
            // The comparison modes affect only the guide and can be used from capture links.
            string mode = string.Empty;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out var page))
                foreach (string entry in page.Query.TrimStart('?').Split('&'))
                    if (entry.StartsWith("throwGuide=", System.StringComparison.Ordinal))
                        mode = entry.Substring("throwGuide=".Length);
#else
            foreach (string argument in System.Environment.GetCommandLineArgs())
                if (argument.StartsWith("-haunterThrowGuide=", System.StringComparison.Ordinal))
                    mode = argument.Substring("-haunterThrowGuide=".Length);
#endif
            return mode == "ring" ? ThrowGuide.Ring : mode == "line" ? ThrowGuide.Line : ThrowGuide.Arc;
        }

        static Vector2[] CreateChargeRing()
        {
            var ring = new Vector2[ChargeRingSegments + 1];
            for (int i = 0; i < ring.Length; i++)
            {
                float angle = Mathf.PI * (2f * i / ChargeRingSegments - .5f);
                ring[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return ring;
        }

        void DrawThrowFeedback()
        {
            if (Event.current.type != EventType.Repaint) return;
            // Use actual pixels so the reticle stays round on portrait touch layouts.
            Matrix4x4 hudMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 600f, .7f, 1.8f);
            if (throwGuide != ThrowGuide.Ring) DrawThrowGuide(scale);

            Vector2 center = new Vector2(Screen.width * .5f, Screen.height * .5f);
            float radius = 21f * scale;
            float completed = Player.ThrowCharge * ChargeRingSegments;
            for (int i = 0; i < ChargeRingSegments; i++)
            {
                Vector2 from = center + ChargeRing[i] * radius;
                Vector2 to = center + ChargeRing[i + 1] * radius;
                Line(from, to, .76f, 4.5f * scale, false);
                Line(from, to, .5f, 1.5f * scale);
                if (i < completed)
                    Line(from, Vector2.Lerp(from, to, completed - i), .98f, 2.5f * scale);
            }
            GUI.matrix = hudMatrix;
        }

        void DrawThrowGuide(float scale)
        {
            Camera camera = Player.ViewCamera;
            Vector3 origin = Player.NominalThrowOrigin;
            Vector3 velocity = Player.PreviewThrowVelocity;
            // The short near-camera offset makes the start readable beside the reticle.
            // It fades out into the game-space launch trajectory.
            Vector3 handSide = camera.transform.right * Mathf.Min(.2f, camera.aspect * .22f);
            const float startTime = .08f;
            const float endTime = 1.15f;
            if (throwGuide == ThrowGuide.Line)
            {
                if (ProjectThrowPoint(camera, origin + velocity * startTime + handSide, out Vector2 start)
                    && ProjectThrowPoint(camera, origin + velocity * endTime, out Vector2 end))
                    Line(start, end, .65f, 1.45f * scale);
                return;
            }

            // Project an unobstructed flight. A sightless aiming aid must not sample
            // collision geometry or suggest where the actual prop will hit.
            const int steps = 24;
            Vector2 last = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i <= steps; i++)
            {
                float progress = i / (float)steps;
                float time = Mathf.Lerp(startTime, endTime, progress);
                float side = 1f - progress;
                Vector3 point = origin + velocity * time + Physics.gravity * (.5f * time * time) + handSide * (side * side);
                if (!ProjectThrowPoint(camera, point, out Vector2 pixel) || (pixel - last).sqrMagnitude < 16f * scale * scale)
                    continue;
                last = pixel;
                float size = Mathf.Lerp(3.8f, 2f, progress) * scale;
                Fill(new Rect(pixel.x - size * .5f - 1f, pixel.y - size * .5f - 1f, size + 2f, size + 2f), .65f, false);
                Fill(new Rect(pixel.x - size * .5f, pixel.y - size * .5f, size, size), Mathf.Lerp(.86f, .32f, progress));
            }
        }

        static bool ProjectThrowPoint(Camera camera, Vector3 point, out Vector2 pixel)
        {
            Vector3 screen = camera.WorldToScreenPoint(point);
            pixel = new Vector2(screen.x, Screen.height - screen.y);
            return screen.z > camera.nearClipPlane;
        }

        void DrawTutorial()
        {
            if (!TouchControls.Enabled)
            {
                const float top = 650f;
                Fill(new Rect(330, top, 940, 137), .94f, false);
                Fill(new Rect(330, top, 940, 1), .38f);
                Text(new Rect(350, top + 13, 900, 22), Tutorial.WorldFrozen ? "GUIDED OPENING  /  TIME FROZEN" : "GUIDED OPENING", centered, .48f);
                GUI.Label(new Rect(350, top + 42, 900, 28), Tutorial.Heading, tutorialHeading);
                Text(new Rect(360, top + 80, 880, 45), Tutorial.Instruction, tutorialCopy, .92f);
                Text(new Rect(1330, 35, 240, 28), "T  SKIP TUTORIAL", small, .65f);
            }
            if (Tutorial.Stage == TutorialStage.Throw || Tutorial.Stage == TutorialStage.FocusThrow)
            {
                Vector3 target = Player.ViewCamera.WorldToViewportPoint(Tutorial.ThrowTarget);
                if (target.z > 0)
                {
                    Vector2 point = new Vector2(target.x * Width, (1f - target.y) * Height);
                    DrawCrosshair(point);
                    Text(new Rect(point.x - 100, point.y - 45, 200, 25), "THROW HERE", centered, .9f);
                }
            }
        }

        void DrawPause()
        {
            DrawHud();
            Fill(new Rect(0, 0, Width, Height), 1f, false);
            Text(new Rect(110, 160, 650, 30), "TAKE A BREATH", label, .5f);
            GUI.Label(new Rect(103, 235, 900, 115), "PAUSED", title);
            GUI.Label(new Rect(112, 377, 700, 60), "The dark can wait.", body);
            if (TouchControls.Enabled)
            {
                DrawEchoArtwork(new Vector2(1180, 460));
                return; // Large HTML buttons provide Resume / Restart / Skip on touch screens.
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (DrawButton(new Rect(110, 490, 330, 60), "RESUME     /     ENTER")) Resume();
#else
            if (DrawButton(new Rect(110, 490, 330, 60), "RESUME     /     ESC")) Resume();
#endif
            if (DrawButton(new Rect(110, 575, 330, 60), LevelNumber == 1 ? "RESTART ARCHIVE" : "RESTART LEVEL")) RetryLevel();
            if (Tutorial != null && Tutorial.Active && DrawButton(new Rect(110, 660, 330, 60), "SKIP TUTORIAL & RESUME"))
            {
                Tutorial.Skip();
                Resume();
            }
            if (DrawButton(new Rect(110, LevelNumber == 1 && Tutorial.Active ? 726 : 660, 330, 36),
                "REDUCED DISTORTION  /  " + (ReducedDistortion ? "ON" : "OFF"))) ToggleDistortion();
            if (DrawButton(new Rect(462, 575, 245, 60), "LEVEL SELECT")) Restart();
            DrawEchoArtwork(new Vector2(1180, 460));
            Text(new Rect(110, 775, 900, 30), "SPACE / RMB  CANE     E  PICK UP     HOLD LMB  THROW     Q  DROP", small, .55f);
            Text(new Rect(110, 811, 1000, 30), "WASD  MOVE     " + PlayerController.CrouchKeys + "  CROUCH     " + PlayerController.SprintKeys + "  RUN     MOUSE  LOOK", small, .55f);
        }

        void DrawOutcome()
        {
            bool escaped = State == SessionState.Escaped;
            Fill(new Rect(0, 0, Width, Height), 1f, false);
            Text(new Rect(110, 140, 800, 30), escaped ? "THE BUILDINGS FALL SILENT" : "SOMETHING HEARD YOU", label, .5f);
            GUI.Label(new Rect(103, 245, 1250, 115), escaped ? "YOU FOUND THE EXIT." : "IT FOUND YOU.", title);
            GUI.Label(new Rect(112, 398, 850, 100), escaped
                ? "One last step. Then the world opens up."
                : "Let the next sound come from somewhere else.\nCrouch to stay quiet. Throw objects to lead it away.", body);
            Fill(new Rect(110, 538, 800, 1), .2f);
            GUI.Label(new Rect(110, 570, 220, 60), FormatTime(ElapsedSeconds), numeral);
            Text(new Rect(110, 630, 220, 22), "TIME IN THE DARK", small, .5f);
            GUI.Label(new Rect(420, 570, 220, 60), SoundsMade.ToString("00"), numeral);
            Text(new Rect(420, 630, 250, 22), "SOUNDS LEFT BEHIND", small, .5f);
            if (!TouchControls.Enabled)
            {
                if (!escaped && DrawButton(new Rect(110, 727, 330, 60), "TRY AGAIN     /     R")) RetryLevel();
                if (DrawButton(new Rect(escaped ? 110 : 462, 727, 245, 60), "LEVEL SELECT")) Restart();
            }
            Text(new Rect(110, 856, 900, 22), "HAUNTER IN THE DARK     /     CONCEPT 01", small, .4f);
        }

        static string FormatTime(float seconds)
        {
            return ((int)seconds / 60).ToString("00") + ":" + ((int)seconds % 60).ToString("00");
        }

        bool DrawButton(Rect rect, string text)
        {
            Fill(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), .65f);
#if UNITY_WEBGL && !UNITY_EDITOR
            // Request pointer lock on press so Unity can complete it on the browser's mouse-up event.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return true;
            }
#endif
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

        static void Line(Vector2 a, Vector2 b, float alpha, float thickness = 1f, bool white = true)
        {
            Matrix4x4 old = GUI.matrix;
            Vector2 delta = b - a;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
            Fill(new Rect(0, -thickness * .5f, delta.magnitude, thickness), alpha, white);
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
