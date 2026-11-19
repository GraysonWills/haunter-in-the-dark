using UnityEngine;

namespace Haunter
{
    // Only the current playable room and its navigation mesh remain active.
    public sealed class HaunterCampaign : MonoBehaviour
    {
        public int Level { get; private set; } = 1;
        public HaunterLevelWorld Room { get; private set; }
        public ListenerCreature SecondCreature { get; private set; }
        public string Title => Level == 2 ? "COURTYARD" : Level == 3 ? "BALLROOM" : Level == 4 ? "FINAL MAZE" : "ARCHIVE";
        public bool CrossingCollapsed { get; private set; }
        internal bool CanLeaveCourtyard => Level != 2 || courtyardStage == CourtyardStage.Roaming;
        public string Hint { get; private set; }
        public string HintHeading { get; private set; }
        public string IntroStage => Level != 2 ? "" : courtyardStage.ToString();
        public bool Quiet => Room && NoiseSystem.Instance && NoiseSystem.Instance.InQuietZone(session.Player.transform.position);

        enum CourtyardStage { Crossing, Collapse, Impact, Guide, Roaming }
        CourtyardStage courtyardStage;
        float stageAt, cueEnds, hintEnds;
        bool savedInput, guiding;
        GameSession session;
        PrototypeWorld archive;
        Material material;
        HaunterAtmosphere atmosphere;

        public void Initialize(GameSession game)
        {
            session = game;
            atmosphere = gameObject.AddComponent<HaunterAtmosphere>();
            atmosphere.Initialize(game, this);
        }

        public bool Enter(int number)
        {
            if (number < 2 || number > 4 || !session.Player || !session.Creature) return false;
            if (!archive) archive = FindFirstObjectByType<PrototypeWorld>();
            if (!archive) return false;
            if (!material) material = archive.EchoSurfaceMaterial;
            if (!material) return false;
            var next = HaunterLevelWorld.Build(number, archive.transform, material);
            if (!next || !next.NavMeshReady)
            {
                if (next) Destroy(next.gameObject);
                return false;
            }

            EndGuide();
            if (session.Tutorial.Active) session.Tutorial.Skip();
            RemoveCreature(session.Creature);
            RemoveCreature(SecondCreature);
            SecondCreature = null;
            session.Exit.gameObject.SetActive(false);
            if (Room)
            {
                Room.SetNavigationActive(false);
                Room.gameObject.SetActive(false);
                Destroy(Room.gameObject);
            }
            else archive.CloseArchive();

            Room = next;
            Level = number;
            CrossingCollapsed = false;
            stageAt = Time.time;
            session.Exit = Room.Exit;
            session.Player.EnterLevel(Room.Spawn);
            TouchControls.Reset();
            NoiseSystem.Instance.ConfigureRoom(number, Room.QuietZones);
            Physics.SyncTransforms();

            session.Creature = SpawnCreature(Room.CreatureSpawn, number == 4 ? "Exit guard" : "Roaming listener");
            if (number == 4)
            {
                session.Creature.ConfigureGuard(Room.CreatureSpawn);
                SecondCreature = SpawnCreature(Room.SecondCreatureSpawn, "Maze roamer");
                SecondCreature.ConfigurePatrol(Room.Patrol);
            }
            else session.Creature.ConfigurePatrol(Room.Patrol);
            if (number == 2)
            {
                courtyardStage = CourtyardStage.Crossing;
                session.Creature.TutorialHold = true;
                session.Creature.CatchSuppressed = true;
                SetHint("THE COURTYARD", "Cross the open yard toward the lower entrance.", 5f);
                NoiseSystem.Emit(Room.Spawn + Vector3.forward * 4f, 1.2f, NoiseKind.Intro);
            }
            else if (number == 3)
            {
                if (BrowserBridge.CaptureShot == 0)
                    Room.gameObject.AddComponent<BallroomIntroduction>().Begin(session, this, Room.Spawn);
            }
            else
            {
                SetHint("THE FINAL MAZE", "One guards the door. One roams. Throw two separate objects to draw them apart.", 9f);
                NoiseSystem.Emit(Room.Spawn + Vector3.forward * 3f, 1f, NoiseKind.Intro);
                Room.PrepareMazeScan(session);
            }
            atmosphere.SetLevel(number);
            return true;
        }

        ListenerCreature SpawnCreature(Vector3 point, string name)
        {
            var creatureObject = new GameObject(name) { layer = 10 };
            creatureObject.transform.SetParent(Room.transform, false);
            creatureObject.transform.SetPositionAndRotation(point + Vector3.up * .05f, Quaternion.identity);
            var creature = creatureObject.AddComponent<ListenerCreature>();
            creature.Initialize(session.Player.transform, material);
            return creature;
        }

        static void RemoveCreature(ListenerCreature creature)
        {
            if (!creature) return;
            creature.gameObject.SetActive(false);
            Destroy(creature.gameObject);
        }

        public Vector3 Bearing
        {
            get
            {
                if (!Room) return session.Exit.position;
                if (Level == 2)
                {
                    if (!CrossingCollapsed) return Room.ApparentCrossing;
                    if (NeedsCourtyardStairs()) return Room.UpstairsApproach;
                }
                return Room.Exit.position;
            }
        }

        bool NeedsCourtyardStairs()
        {
            Vector3 player = session.Player.transform.position;
            Vector3 fromFoot = player - Room.UpstairsApproach;
            bool onStairLane = Mathf.Abs(fromFoot.x) < 1.5f
                && (Mathf.Abs(fromFoot.z) < 1.6f || (fromFoot.z > 0f && fromFoot.y > .22f));
            return player.y < Room.Exit.position.y - .9f && !onStairLane;
        }

        public string Objective
        {
            get
            {
                if (Level == 2)
                {
                    if (!CrossingCollapsed) return "REACH THE CROSSING";
                    return NeedsCourtyardStairs() && session.Player.transform.position.z > Room.UpstairsApproach.z + 1.3f
                        ? "BACKTRACK TO THE SIDE STAIRS" : "FIND THE UPSTAIRS ENTRANCE";
                }
                if (Level == 3) return "FIND THE FAR DOOR";
                if (Level == 4) return "LEAD BOTH AWAY FROM THE EXIT";
                return "FIND THE EXIT";
            }
        }

        void FixedUpdate()
        {
            if (Room && session.State == SessionState.Playing &&
                Room.ExitThreshold.bounds.Contains(session.Player.transform.position + Vector3.up * .8f))
                session.ReachExit();
        }

        void Update()
        {
            if (!Room || session.State != SessionState.Playing) return;
            if (Time.time >= hintEnds) { Hint = ""; HintHeading = ""; }
            if (Level != 2 || courtyardStage == CourtyardStage.Roaming) return;
            if (courtyardStage == CourtyardStage.Crossing && Room.HasReachedCrossing(session.Player.transform.position))
            {
                savedInput = session.Player.AcceptInput;
                session.Player.AcceptInput = false;
                session.Player.CancelThrow();
                TouchControls.Reset();
                guiding = true;
                Room.ShowCrossingOutline(true);
                courtyardStage = CourtyardStage.Collapse;
                stageAt = Time.time;
                NoiseSystem.Emit(Room.CollapsePosition + Vector3.up * 2f, .7f, NoiseKind.Intro);
                SetHint("THE CROSSING IS GIVING WAY", "The canopy and its supports are coming down.", 3f);
            }
            if (courtyardStage == CourtyardStage.Collapse)
            {
                float passed = Time.time - stageAt;
                session.Player.GuideLookAt(Room.CollapsePosition + Vector3.up * 1.8f,
                    1f - Mathf.Exp(-9f * Time.deltaTime));
                if (passed < HaunterLevelWorld.CollapseDuration)
                {
                    Room.AnimateCollapse(passed);
                    return;
                }
                var creature = session.Creature;
                creature.SetNavigationActive(false);
                Room.FinishCollapse();
                creature.SetNavigationActive(true);
                CrossingCollapsed = true;
                courtyardStage = CourtyardStage.Impact;
                stageAt = Time.time;
                NoiseSystem.Emit(Room.CollapsePosition, 2f, NoiseKind.Collapse);
                SetHint("THE CROSSING HAS FALLEN", "The crash carries back to the archive.", 3f);
            }
            else if (courtyardStage == CourtyardStage.Impact && Time.time >= stageAt + .55f)
            {
                Room.ShowCrossingOutline(false);
                var creature = session.Creature;
                creature.TutorialHold = false;
                creature.BeginScriptedInvestigation(Room.CollapsePosition);
                creature.CueTutorialPaw();
                courtyardStage = CourtyardStage.Guide;
                cueEnds = Time.time + 1.05f;
                SetHint("IT HEARD THE CRASH", "Head back around the yard to the side stairs.", 7f);
            }
            if (courtyardStage == CourtyardStage.Guide)
            {
                session.Player.GuideLookAt(session.Creature.transform.position + Vector3.up * .8f,
                    1f - Mathf.Exp(-8f * Time.deltaTime));
                if (Time.time >= cueEnds)
                {
                    EndGuide();
                    courtyardStage = CourtyardStage.Roaming;
                }
            }
        }

        void EndGuide()
        {
            if (!guiding) return;
            guiding = false;
            session.Player.AcceptInput = savedInput;
            TouchControls.Reset();
            if (session.Creature) session.Creature.CatchSuppressed = false;
        }

        internal void SetHint(string heading, string text, float seconds)
        {
            HintHeading = heading;
            Hint = text;
            hintEnds = Time.time + seconds;
        }
    }
}
