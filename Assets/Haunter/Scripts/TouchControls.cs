using System.Runtime.InteropServices;
using UnityEngine;

namespace Haunter
{
    /// <summary>One browser input snapshot per frame, shared by the session and player.</summary>
    public static class TouchControls
    {
        public const int Cane = 1, Pickup = 2, Drop = 4, ThrowHeld = 8, ThrowReleased = 16,
            Crouch = 32, Sprint = 64, Pause = 128, CancelThrow = 256;
        static int frame = -1, buttons;
        static Vector2 move, look;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int Haunter_TouchEnabled();
        [DllImport("__Internal")] static extern float Haunter_TouchAxis(int axis);
        [DllImport("__Internal")] static extern int Haunter_TouchButtons();
        [DllImport("__Internal")] static extern void Haunter_ResetTouchInput();
        [DllImport("__Internal")] static extern void Haunter_UpdateTouchState(int state, int actions, int held, int nearby, float charge);
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
            if (nearby == 0 && player != null && session.Exit != null
                && PlayerController.HorizontalDistance(player.transform.position, session.Exit.position) < 1.9f)
                nearby = 2;
            Haunter_UpdateTouchState((int)session.State, player != null ? (int)player.AllowedActions : 0,
                player != null && player.HeldObject != null ? 1 : 0, nearby,
                player != null ? player.ThrowCharge : 0f);
#endif
        }
    }
}
