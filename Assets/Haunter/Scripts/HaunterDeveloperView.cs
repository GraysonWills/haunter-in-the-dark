using UnityEngine;

namespace Haunter
{
    // An opt-in developer camera. Its marks describe the listener's last audible evidence.
    public sealed class HaunterDeveloperView : MonoBehaviour
    {
        public static bool IsActive { get; private set; }
        GameSession session;
        Camera playerView, observer;
        AudioListener playerAudio, observerAudio;
        GUIStyle heading, copy, marker;

        void Awake()
        {
            session = GetComponent<GameSession>();
            if (!session || !session.Player || !session.Player.ViewCamera) return;
            playerView = session.Player.ViewCamera;
            playerAudio = playerView.GetComponent<AudioListener>();
            var rig = new GameObject("Listener developer camera");
            rig.transform.SetParent(transform, false);
            observer = rig.AddComponent<Camera>();
            observer.CopyFrom(playerView);
            observer.cullingMask &= ~(1 << 9);
            observer.enabled = false;
            observerAudio = rig.AddComponent<AudioListener>();
            observerAudio.enabled = false;
            rig.AddComponent<EchoRenderer>().OutlineShader = playerView.GetComponent<EchoRenderer>().OutlineShader;
        }

        void LateUpdate()
        {
            if (!observer || !playerView) return;
            var listener = session.Creature;
            bool show = session.State == SessionState.Playing && session.LevelNumber > 1 && listener;
            playerView.enabled = !show;
            observer.enabled = show;
            if (playerAudio) playerAudio.enabled = !show;
            observerAudio.enabled = show;
            IsActive = show;
            if (!show) return;
            var forward = listener.transform.forward;
            var pivot = listener.transform.position + Vector3.up * 1.25f;
            var desired = pivot - forward * 2.75f + Vector3.up * 1.05f;
            if (Physics.Linecast(pivot, desired, out var obstruction, 1, QueryTriggerInteraction.Ignore))
                desired = obstruction.point + obstruction.normal * .17f;
            observer.transform.position = desired;
            observer.transform.LookAt(pivot + forward * 4f);
        }

        void OnDisable()
        {
            IsActive = false;
            if (playerView) playerView.enabled = true;
            if (observer) observer.enabled = false;
            if (observerAudio) observerAudio.enabled = false;
            if (playerAudio) playerAudio.enabled = true;
        }

        void OnGUI()
        {
            if (!IsActive || !observer || !session.Creature) return;
            if (heading == null)
            {
                heading = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 16 };
                copy = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                marker = new GUIStyle(copy) { alignment = TextAnchor.MiddleCenter };
            }
            var listener = session.Creature;
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            GUI.Box(new Rect(22, 22, 405, 108), "");
            GUI.Label(new Rect(34, 30, 395, 23), "DEVELOPER VIEW  /  LISTENER", heading);
            GUI.Label(new Rect(34, 56, 385, 22), listener.Role + "   " + listener.BehaviorName, copy);
            string evidence = listener.HasHeardSound && listener.HearingConfidence > .01f
                ? "Audible estimate  " + Mathf.RoundToInt(listener.HearingConfidence * 100) + "% confidence"
                : "No current audible estimate";
            GUI.Label(new Rect(34, 78, 385, 22), evidence, copy);
            if (listener.IsLingeringAtDistraction)
                GUI.Label(new Rect(34, 101, 385, 21), "Investigation  " + listener.DistractionTimeRemaining.ToString("0.0") + " s left", copy);
            bool marksEstimate = listener.HasHeardSound && listener.HearingConfidence > .01f;
            if (marksEstimate)
            {
                DrawMark(listener.LastHeardPosition, listener.LastHeardReflection ? "APPARENT REFLECTION" : "HEARD LOCATION");
                if (listener.LastHeardReflection) DrawMark(listener.LastHeardOrigin, "REAL SOURCE  /  DEBUG");
            }
            if (listener.IsInvestigating && (!marksEstimate ||
                (listener.TargetPosition - listener.LastHeardPosition).sqrMagnitude > .45f * .45f))
                DrawMark(listener.TargetPosition, "CURRENT INVESTIGATION");
            GUI.matrix = previous;
        }

        void DrawMark(Vector3 position, string name)
        {
            var point = observer.WorldToViewportPoint(position + Vector3.up * .22f);
            if (point.z < 0 || point.x < .08f || point.x > .92f || point.y < .06f || point.y > .93f) return;
            var rect = new Rect(point.x * 1280f - 110f, (1f - point.y) * 720f - 12f, 220f, 26f);
            GUI.Box(rect, "");
            GUI.Label(rect, "·  " + name, marker);
        }
    }
}
