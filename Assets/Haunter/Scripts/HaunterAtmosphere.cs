using UnityEngine;

namespace Haunter
{
    public sealed class HaunterAtmosphere : MonoBehaviour
    {
        static readonly string[] Rooms = { "Archive", "Courtyard", "Ballroom", "Maze" };
        static readonly string[] Moods = { "Suspicion", "Investigation", "Pursuit", "Release" };
        readonly AudioClip[] cues = new AudioClip[Moods.Length];
        GameSession session;
        HaunterCampaign campaign;
        AudioSource room, score;
        float lastAudiblePaw = -30f;
        float changeAt, releaseUntil;
        int previousThreat = -1, playingCue = -1;

        public void Initialize(GameSession game, HaunterCampaign levels)
        {
            session = game;
            campaign = levels;
        }

        void Start()
        {
            if (!NoiseSystem.Instance || !NoiseSystem.Instance.PlaybackAvailable) return;
            room = AddVoice();
            score = AddVoice();
            for (int i = 0; i < cues.Length; i++)
                cues[i] = Resources.Load<AudioClip>("HaunterSounds/" + Moods[i]);
            SetLevel(campaign.Level);
        }

        AudioSource AddVoice()
        {
            var voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = true;
            voice.spatialBlend = 0f;
            voice.volume = 0f;
            return voice;
        }

        public void SetLevel(int number)
        {
            if (!room) return;
            room.Stop();
            room.clip = Resources.Load<AudioClip>("HaunterSounds/" + Rooms[number - 1]);
            if (room.clip) room.Play();
            lastAudiblePaw = -30f;
            previousThreat = -1;
            releaseUntil = 0;
        }

        void OnEnable() { NoiseSystem.Emitted += OnNoise; }
        void OnDisable() { NoiseSystem.Emitted -= OnNoise; }

        void OnNoise(NoiseEvent noise)
        {
            if (noise.Kind != NoiseKind.Creature || !session || !session.Player) return;
            var player = session.Player.transform.position;
            if ((player - noise.Position).sqrMagnitude < 100f) lastAudiblePaw = Time.time;
        }

        void Update()
        {
            if (!room || !session) return;
            bool active = session.State == SessionState.Playing || session.State == SessionState.Escaped;
            float step = Time.unscaledDeltaTime * .25f;
            float levelGain = campaign.Quiet ? .045f : .095f;
            room.volume = Mathf.MoveTowards(room.volume, active ? levelGain : 0f, step);
            int threat = -1;
            if (session.State == SessionState.Playing && Time.time - lastAudiblePaw < 4f)
                threat = Mathf.Max(Threat(session.Creature), Threat(campaign.SecondCreature));
            if (previousThreat > 0 && threat < 0) releaseUntil = Time.time + 2.5f;
            previousThreat = threat;
            int desired = threat < 0 && (Time.time < releaseUntil || session.State == SessionState.Escaped) ? 3 : threat;
            if (active && desired != playingCue && Time.unscaledTime >= changeAt)
            {
                if (desired >= 0)
                {
                    score.Stop();
                    score.clip = cues[desired];
                    score.volume = 0f;
                    if (score.clip) score.Play();
                }
                playingCue = desired;
                changeAt = Time.unscaledTime + .45f;
            }
            float target = active && desired >= 0 ? (desired == 2 ? .13f : .075f) : 0f;
            score.volume = Mathf.MoveTowards(score.volume, target, step);
        }

        static int Threat(ListenerCreature creature)
        {
            if (!creature) return -1;
            if (creature.IsCommittedToDistraction) return 1;
            if (!creature.IsInvestigating) return -1;
            return creature.HearingConfidence < .76f ? 0 : 2;
        }
    }
}
