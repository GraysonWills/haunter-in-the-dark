using System.Collections.Generic;
using UnityEngine;

namespace Haunter
{
    /// <summary>A short overhead echo of the maze's fixed walls, player heading and exit.</summary>
    public sealed class MazeScan : MonoBehaviour
    {
        const float Duration = 3.3f;
        const float SweepDuration = 1.05f;
        readonly List<Bounds> walls = new List<Bounds>();
        GameSession session;
        Bounds area;
        bool showOpening, previousInput;
        float openedAt, previousTimeScale;
        GUIStyle heading, caption, legend, marker;

        public bool Active { get; private set; }

        public void Initialize(GameSession game, Transform architecture, Bounds mapArea)
        {
            session = game;
            area = mapArea;
            float eyeHeight = game.Player.transform.position.y + 1.8f;
            foreach (BoxCollider barrier in architecture.GetComponentsInChildren<BoxCollider>())
            {
                Bounds bounds = barrier.bounds;
                // Overhead details above head height and floor markings do not define lanes.
                if (barrier.enabled && bounds.min.y < eyeHeight && bounds.size.y > .6f)
                    walls.Add(bounds);
            }
            showOpening = BrowserBridge.CaptureShot == 0;
        }

        void Update()
        {
            if (!session) return;
            if (session.State != SessionState.Playing)
            {
                if (Active) Close();
                return;
            }

            if (Active)
            {
                if (Time.unscaledTime >= openedAt + Duration) Close();
                return;
            }
            if (showOpening || Input.GetKeyDown(KeyCode.M) || TouchControls.Pressed(TouchControls.Scan))
            {
                showOpening = false;
                TryOpen();
            }
        }

        public bool TryOpen()
        {
            if (Active || !session || session.State != SessionState.Playing || Time.timeScale <= 0f)
                return false;
            Active = true;
            openedAt = Time.unscaledTime;
            previousInput = session.Player.AcceptInput;
            previousTimeScale = Time.timeScale;
            session.Player.AcceptInput = false;
            session.Player.CancelThrow();
            TouchControls.Reset();
            Time.timeScale = 0f;
            return true;
        }

        void Close()
        {
            if (!Active) return;
            Active = false;
            if (!session || !session.Player) return;
            session.Player.AcceptInput = previousInput;
            if (session.State == SessionState.Playing) Time.timeScale = previousTimeScale;
            TouchControls.Reset();
        }

        void OnDisable() => Close();

        void OnGUI()
        {
            if (!session || session.State != SessionState.Playing || Event.current.type != EventType.Repaint)
                return;
            if (!Active && (TouchControls.Enabled || HaunterDeveloperView.IsActive)) return;

            PrepareStyles();
            Matrix4x4 matrix = GUI.matrix;
            int depth = GUI.depth;
            Color color = GUI.color;
            float dpi = Mathf.Clamp(Mathf.Min(Screen.width / 760f, Screen.height / 480f), .5f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(dpi, dpi, 1f));
            if (Active)
            {
                GUI.depth = -9;
                DrawOverview(Screen.width / dpi, Screen.height / dpi);
            }
            else
            {
                // Align under the ordinary level label without changing the normal HUD.
                float left = 30f * Screen.width / (1000f * dpi);
                float top = 72f * Screen.height / (900f * dpi);
                GUI.Label(new Rect(left, top, 150f, 22f), "[M]  MAZE SCAN", caption);
            }
            GUI.color = color;
            GUI.depth = depth;
            GUI.matrix = matrix;
        }

        void DrawOverview(float width, float height)
        {
            Fill(new Rect(0, 0, width, height), new Color(.014f, .014f, .014f));
            float heightOnScreen = Mathf.Min(height - 122f, (width - 60f) * area.size.z / area.size.x, 470f);
            float widthOnScreen = heightOnScreen * area.size.x / area.size.z;
            var map = new Rect((width - widthOnScreen) * .5f, (height + 10f - heightOnScreen) * .5f,
                widthOnScreen, heightOnScreen);
            float progress = Mathf.Clamp01((Time.unscaledTime - openedAt) / SweepDuration);
            float sweep = Mathf.Lerp(map.yMax, map.yMin, progress);
            GUI.Label(new Rect(0, map.yMin - 53f, width, 37f), "MAZE SCAN", heading);
            GUI.Label(new Rect(map.x, map.yMin - 18f, map.width, 18f), "NORTH", legend);
            Outline(map, new Color(.24f, .24f, .24f));
            foreach (Bounds wall in walls)
            {
                Vector2 northwest = Project(wall.min.x, wall.max.z, map);
                Vector2 southeast = Project(wall.max.x, wall.min.z, map);
                if (southeast.y < sweep) continue;
                var trace = Rect.MinMaxRect(northwest.x, Mathf.Max(northwest.y, sweep), southeast.x, southeast.y);
                float intensity = Mathf.Lerp(.72f, 1f, 1f - Mathf.Clamp01((trace.y - sweep) / 42f));
                Fill(trace, new Color(intensity * .15f, intensity * .15f, intensity * .15f));
                Outline(trace, new Color(intensity, intensity, intensity));
            }
            if (progress < 1f)
                Fill(new Rect(map.x, sweep, map.width, 2f), new Color(.84f, .84f, .84f));

            Vector3 playerPosition = session.Player.transform.position;
            Vector2 player = Project(playerPosition.x, playerPosition.z, map);
            Vector3 direction = session.Player.transform.forward;
            Vector2 front = new Vector2(direction.x, -direction.z).normalized;
            DrawDirection(player, front);
            GUI.Label(new Rect(player.x + 11f, player.y - 9f, 43f, 18f), "YOU", marker);
            Vector3 exitPosition = session.Exit.position;
            Vector2 exit = Project(exitPosition.x, exitPosition.z, map);
            Outline(new Rect(exit.x - 4f, exit.y - 4f, 8f, 8f), Color.white);
            GUI.Label(new Rect(exit.x + 10f, exit.y - 9f, 50f, 18f), "EXIT", marker);

            string replay = TouchControls.Enabled ? "Tap Maze scan to replay" : "Press M to replay";
            GUI.Label(new Rect(0, map.yMax + 11f, width, 24f), replay, legend);
        }

        Vector2 Project(float x, float z, Rect map) => new Vector2(
            Mathf.Lerp(map.xMin, map.xMax, Mathf.InverseLerp(area.min.x, area.max.x, x)),
            Mathf.Lerp(map.yMax, map.yMin, Mathf.InverseLerp(area.min.z, area.max.z, z)));

        void PrepareStyles()
        {
            if (heading != null) return;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            marker = new GUIStyle { font = font, fontStyle = FontStyle.Bold, fontSize = 12 };
            marker.normal.textColor = Color.white;
            caption = new GUIStyle(marker) { fontSize = 13 };
            legend = new GUIStyle(marker) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            heading = new GUIStyle(legend) { fontSize = 29 };
        }

        static void DrawDirection(Vector2 point, Vector2 direction)
        {
            Vector2 cross = new Vector2(-direction.y, direction.x) * 4f;
            Vector2 tip = point + direction * 9f;
            Vector2 tail = point - direction * 4f;
            DrawLine(tip, tail - cross);
            DrawLine(tip, tail + cross);
            DrawLine(tail - cross, tail + cross);
        }

        static void DrawLine(Vector2 from, Vector2 to)
        {
            Matrix4x4 matrix = GUI.matrix;
            Vector2 delta = to - from;
            GUI.matrix = matrix * Matrix4x4.TRS(new Vector3(from.x, from.y, 0f),
                Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
            Fill(new Rect(0f, -.75f, delta.magnitude, 1.5f), Color.white);
            GUI.matrix = matrix;
        }

        static void Outline(Rect rect, Color color)
        {
            Fill(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
            Fill(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
            Fill(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
            Fill(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
        }

        static void Fill(Rect rect, Color color)
        {
            Color before = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }
    }
}
