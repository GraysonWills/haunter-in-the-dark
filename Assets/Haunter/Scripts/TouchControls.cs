using System.Runtime.InteropServices;
using UnityEngine;

namespace Haunter
{
    /// <summary>One browser input snapshot per frame, shared by the session and player.</summary>
    public static class TouchControls
    {
        public const int Cane = 1, Pickup = 2, Drop = 4, ThrowHeld = 8, ThrowReleased = 16,
            Crouch = 32, Sprint = 64, Pause = 128, CancelThrow = 256, Scan = 512;
        static int frame = -1, buttons;
        static Vector2 move, look;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int Haunter_TouchEnabled();
        [DllImport("__Internal")] static extern float Haunter_TouchAxis(int axis);
        [DllImport("__Internal")] static extern int Haunter_TouchButtons();
        [DllImport("__Internal")] static extern void Haunter_ResetTouchInput();
        [DllImport("__Internal")] static extern void Haunter_UpdateTouchState(int state, int actions, int held, int nearby,
            float charge, int menuFlags, int checkpoint, int level);
        [DllImport("__Internal")] static extern void Haunter_UpdateTouchTutorial(string heading, string instruction, int frozen);
        static TutorialDirector lastTutorial;
        static TutorialStage lastStage;
        static bool lastCrouched, tutorialVisible;
        static string lastHint, lastHintHeading;
#endif

        public static bool Enabled
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_TouchEnabled() != 0;
#else
                return false;
#endif
            }
        }

        static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            buttons = 0;
            move = look = Vector2.zero;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!Enabled) return;
            move = Vector2.ClampMagnitude(new Vector2(Haunter_TouchAxis(0), Haunter_TouchAxis(1)), 1f);
            look = new Vector2(Haunter_TouchAxis(2), Haunter_TouchAxis(3));
            buttons = Haunter_TouchButtons();
#endif
        }

        public static Vector2 Move { get { Poll(); return move; } }
        public static Vector2 Look { get { Poll(); return look; } }
        public static bool Pressed(int button) { Poll(); return (buttons & button) != 0; }

        public static void Reset()
        {
            // Keep this frame empty even if another component reads after a state transition.
            frame = Time.frameCount;
            buttons = 0;
            move = look = Vector2.zero;
#if UNITY_WEBGL && !UNITY_EDITOR
            Haunter_ResetTouchInput();
#endif
        }

        public static void Publish(GameSession session)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var player = session.Player;
            int nearby = player != null && player.NearbyObject != null ? 1 : 0;
            if (nearby == 0 && session.PlayerNearExit(1.9f))
                nearby = 2;
            int menuFlags = (session.Tutorial != null && session.Tutorial.Active ? 1 : 0)
                | (session.ReducedDistortion ? 2 : 0);
            var room = session.Campaign.Room;
            if (room && room.MazeOverview)
                menuFlags |= 4 | (room.MazeOverview.Active ? 8 : 0);
            Haunter_UpdateTouchState((int)session.State, player != null ? (int)player.AllowedActions : 0,
                player != null && player.HeldObject != null ? 1 : 0, nearby,
                player != null ? player.ThrowCharge : 0f, menuFlags, session.CheckpointLevel, session.LevelNumber);
            var tutorial = session.Tutorial;
            if (!Enabled || tutorial == null || !tutorial.Active)
            {
                string hint = Enabled ? session.Campaign.Hint ?? string.Empty : string.Empty;
                string heading = Enabled ? session.Campaign.HintHeading ?? string.Empty : string.Empty;
                if (tutorialVisible || hint != lastHint || heading != lastHintHeading)
                    Haunter_UpdateTouchTutorial(heading, hint, 0);
                tutorialVisible = false;
                lastHint = hint;
                lastHintHeading = heading;
                return;
            }
            if (!tutorialVisible || tutorial != lastTutorial || tutorial.Stage != lastStage || tutorial.HasCrouched != lastCrouched)
            {
                Haunter_UpdateTouchTutorial(tutorial.Heading, tutorial.Instruction, tutorial.WorldFrozen ? 1 : 0);
                tutorialVisible = true;
                lastTutorial = tutorial;
                lastStage = tutorial.Stage;
                lastCrouched = tutorial.HasCrouched;
            }
#endif
        }
    }
}
