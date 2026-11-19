using System.Collections;
using UnityEngine;

namespace Haunter
{
    // The entry lesson belongs to this room. Leaving the room cancels the remaining cues.
    public sealed class BallroomIntroduction : MonoBehaviour
    {
        internal void Begin(GameSession session, HaunterCampaign campaign, Vector3 entrance)
            => StartCoroutine(ShowContrast(session, campaign, entrance));

        static IEnumerator ShowContrast(GameSession session, HaunterCampaign campaign, Vector3 entrance)
        {
            yield return new WaitUntil(() => session.State == SessionState.Playing);
            var player = session.Player;
            var noise = NoiseSystem.Instance;
            campaign.SetHint("THE BALLROOM", "Tap your cane to trace the furniture ahead.", 8f);
            float demonstrationAt = Time.time + 6.5f;
            yield return new WaitUntil(() => session.State == SessionState.Playing &&
                (player.CaneReady < .9f || player.transform.position.z > entrance.z + 2.5f ||
                Time.time >= demonstrationAt));

            if (player.CaneReady >= .9f)
                noise.ShowBallroomEcho(player.transform.position + Vector3.up * .08f, .96f, 0f);
            campaign.SetHint("ONE CLEAN ECHO", "The furniture stays where the outline first appears.", 2f);
            yield return new WaitForSeconds(1.55f);

            campaign.SetHint("ECHOES OVERLAP", session.ReducedDistortion
                ? "Reduced distortion keeps the outlines steady. Weave around the furniture."
                : "Distant outlines split and bend. Weave around the furniture.", 4.1f);
            noise.ShowBallroomEcho(entrance + new Vector3(-2.65f, .07f, 6.8f), 1.05f, .43f);
            yield return new WaitForSeconds(.42f);
            noise.ShowBallroomEcho(entrance + new Vector3(2.9f, .1f, 8.45f), 1.1f, .72f);
            yield return new WaitForSeconds(.38f);
            noise.ShowBallroomEcho(entrance + new Vector3(.6f, .13f, 11.9f), 1.22f, .98f);
            yield return new WaitForSeconds(3f);

            campaign.SetHint("LET THE ECHOES FADE", "Quiet steps or a side alcove help the extra outlines settle.", 5.5f);
        }
    }
}
