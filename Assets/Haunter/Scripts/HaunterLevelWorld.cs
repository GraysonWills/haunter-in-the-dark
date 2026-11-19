using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    /// <summary>Builds and owns the architecture, loose objects and navigation of one expansion level.</summary>
    public sealed class HaunterLevelWorld : MonoBehaviour
    {
        public int Number { get; private set; }
        public Vector3 Spawn { get; private set; }
        public Transform Exit { get; private set; }
        public BoxCollider ExitThreshold { get; private set; }
        public Vector3 ApparentCrossing { get; private set; }
        internal Vector3 CrossingLookout { get; private set; }
        public Vector3 CollapsePosition { get; private set; }
        public Vector3 UpstairsApproach { get; private set; }
        public Vector3 CreatureSpawn { get; private set; }
        public Vector3 SecondCreatureSpawn { get; private set; }
        public Vector3[] Patrol { get; private set; }
        public Bounds[] QuietZones { get; private set; } = System.Array.Empty<Bounds>();
        public Vector3 FinalStaging { get; private set; }
        public Vector3 PrimaryLure { get; private set; }
        public Vector3 SecondaryLure { get; private set; }
        public MazeScan MazeOverview { get; private set; }
        public bool NavMeshReady => navigationInstance.valid;

        private Transform architecture;
        private Transform props;
        private Material surface;
        internal const float CollapseDuration = 2.7f;
        private Transform crossingArch;
        private CrossingPiece[] crossingPieces;

        private struct CrossingPiece
        {
            public Transform Transform;
            public Collider Collider;
            public Vector3 Standing, Fallen;
            public Quaternion FallenRotation;
            public float Delay, Duration;
        }

        private Bounds navigationBounds;
        private NavMeshData navigationData;
        private NavMeshDataInstance navigationInstance;
        private bool navigationEnabled;

        public static HaunterLevelWorld Build(int number, Transform parent, Material material)
        {
            if (number < 2 || number > 4) throw new System.ArgumentOutOfRangeException(nameof(number));
            var root = new GameObject(number == 2 ? "Courtyard" : number == 3 ? "Ballroom" : "Service maze");
            root.transform.SetParent(parent, false);
            var world = root.AddComponent<HaunterLevelWorld>();
            world.Number = number;
            world.surface = material;
            world.architecture = Group(root.transform, "Fixed architecture");
            world.props = Group(root.transform, "Loose throwables");
            switch (number)
            {
                case 2: world.BuildCourtyard(); break;
                case 3: world.BuildBallroom(); break;
                case 4: world.BuildMaze(); break;
            }
            world.SetNavigationActive(true);
            return world;
        }

        public void SetNavigationActive(bool active)
        {
            navigationEnabled = active;
            if (!active) RemoveNavigation();
            else if (isActiveAndEnabled) AddNavigation();
        }

        internal bool HasReachedCrossing(Vector3 player)
            => player.z >= (Spawn.z + ApparentCrossing.z) * .5f;

        internal void ShowCrossingOutline(bool visible) => EchoMaterial.SetPersistent(crossingArch.gameObject, visible);

        internal void AnimateCollapse(float seconds)
        {
            foreach (var piece in crossingPieces)
            {
                float progress = Mathf.Clamp01((seconds - piece.Delay) / piece.Duration);
                Vector3 position = Vector3.Lerp(piece.Standing, piece.Fallen, progress * Mathf.Sqrt(progress));
                if (seconds < piece.Delay)
                    position.x += Mathf.Sin(seconds * 38f + piece.Delay * 14f) * .035f;
                piece.Transform.localPosition = position;
                piece.Transform.localRotation = Quaternion.Slerp(Quaternion.identity, piece.FallenRotation,
                    Mathf.SmoothStep(0f, 1f, progress));
            }
        }

        internal void FinishCollapse()
        {
            AnimateCollapse(CollapseDuration);
            foreach (var piece in crossingPieces)
                if (piece.Collider) piece.Collider.enabled = true;
            RemoveNavigation();
            if (navigationData != null) Destroy(navigationData);
            navigationData = null;
            if (navigationEnabled && isActiveAndEnabled) AddNavigation();
        }

        private void OnEnable()
        {
            if (navigationEnabled) AddNavigation();
        }

        private void OnDisable() => RemoveNavigation();

        private void OnDestroy()
        {
            RemoveNavigation();
            if (navigationData != null) Destroy(navigationData);
        }

        private void AddNavigation()
        {
            if (navigationInstance.valid) return;
            if (navigationData == null)
            {
                Physics.SyncTransforms();
                var sources = new List<NavMeshBuildSource>();
                NavMeshBuilder.CollectSources(architecture, 1, NavMeshCollectGeometry.PhysicsColliders,
                    0, new List<NavMeshBuildMarkup>(), sources);
                var settings = NavMesh.GetSettingsByID(0);
                settings.agentRadius = .55f;
                settings.agentHeight = 1.6f;
                settings.agentClimb = .25f;
                settings.agentSlope = 45f;
                settings.overrideVoxelSize = true;
                settings.voxelSize = .12f;
                settings.overrideTileSize = true;
                settings.tileSize = 128;
                navigationData = NavMeshBuilder.BuildNavMeshData(settings, sources, navigationBounds,
                    Vector3.zero, Quaternion.identity);
                if (navigationData == null)
                {
                    Debug.LogError("Haunter: navigation generation failed for level " + Number);
                    return;
                }
            }
            navigationInstance = NavMesh.AddNavMeshData(navigationData);
        }

        private void RemoveNavigation()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
        }

        private void BuildCourtyard()
        {
            Spawn = World(2.8f, -13.3f);
            CreatureSpawn = World(-4, -17.05f);
            ApparentCrossing = World(0, 12);
            CrossingLookout = World(2.8f, .18f);
            CollapsePosition = World(0, 13.3f, .2f);
            UpstairsApproach = World(10, -7.2f);
            Patrol = WorldPoints(new Vector2(-7.4f, 10), new Vector2(-10.2f, 3), new Vector2(-9.5f, -8),
                new Vector2(-2.8f, -9.7f), new Vector2(5.2f, -6.7f), new Vector2(5.6f, 3.3f), new Vector2(5.7f, 10.2f));
            navigationBounds = new Bounds(World(0, -1.7f, 2.5f), new Vector3(29, 12, 36));

            Floor("Open cobbled courtyard", new Vector3(0, -.17f, -.7f), new Vector3(26, .34f, 30.6f));
            SideWalls(13.1f, -15.6f, 14.2f, 4.2f);
            WallSpan("Archive façade, west", -13.2f, -5.36f, -15.7f, 4.4f);
            WallSpan("Archive façade, east", -2.64f, 13.2f, -15.7f, 4.4f);
            Box("Service arch lintel", V(-4, 3.65f, -15.7f), V(2.72f, 1.5f, .38f));
            Floor("Dark archive service entry", V(-4, -.17f, -16.99f), V(2.73f, .34f, 2.68f));
            foreach (float x in new[] { -5.43f, -2.57f })
                Box("Archive recess side", V(x, 1.5f, -17.07f), V(.25f, 3, 2.72f));
            WallSpan("Archive recess back", -5.56f, -2.44f, -18.37f, 3);
            DoorTrim("Archive door", 2.8f, -15.47f, 0, 3, false);
            DoorTrim("Archive service arch", -4, -15.47f, 0, 2.6f, false);

            WallSpan("North façade, west", -13.2f, -1.75f, 14.2f, 5.6f);
            WallSpan("North façade, center", 1.75f, 8.65f, 14.2f, 5.6f);
            WallSpan("North façade, east", 11.35f, 13.2f, 14.2f, 5.6f);
            Box("Balcony foundation", V(10, 1.4f, 14.2f), V(2.7f, 2.8f, .38f));
            Floor("Lower crossing stones", V(0, -.073f, 9.6f), V(3.4f, .18f, 8.9f), false);
            BuildCollapsibleCrossing();
            DoorTrim("Upper service entrance", 10, 13.97f, 2.8f, 2.55f, true);

            var fountain = PrototypeWorld.Primitive("Dry fountain basin", PrimitiveType.Cylinder, architecture,
                V(-.4f, .36f, 1.1f), V(3.1f, .36f, 3.1f), surface, true);
            PrototypeWorld.Primitive("Fountain pedestal", PrimitiveType.Cylinder, architecture,
                V(-.4f, 1.05f, 1.1f), V(.68f, .66f, .68f), surface, false);
            // Remove the capsule-shaped stock collider in favor of the basin's straight collision envelope.
            fountain.GetComponent<Collider>().enabled = false;
            var basin = fountain.AddComponent<BoxCollider>();
            basin.size = new Vector3(.88f, 2, .88f);
            Planter("West avenue planter", -7.3f, -3.2f, 2.7f, 4.1f);
            Planter("East avenue planter", 5.1f, .7f, 1.8f, 3.3f);
            Box("West stone screen", V(-4.7f, .65f, 6.9f), V(3.6f, 1.3f, .5f));
            Box("Covered service bench", V(7.3f, .46f, -8.9f), V(1.4f, .92f, 2.5f));
            for (int z = -11; z <= 10; z += 7)
            {
                Column("West arcade pier", -10.8f, z, 3.4f);
                Box("West arcade architrave", V(-10.8f, 3.4f, z + 2.1f), V(.67f, .28f, 4.65f), false);
            }

            Ramp(V(10, 0, -6), V(10, 2.8f, 4), 2.8f);
            Floor("Upper service balcony", V(10, 2.63f, 9.1f), V(2.8f, .34f, 10.3f));
            Box("Balcony inner rail", V(8.57f, 3.26f, 9), V(.14f, .92f, 10));
            Box("Balcony outer rail", V(11.43f, 3.26f, 9), V(.14f, .92f, 10));
            foreach (float z in new[] { 5f, 9.2f, 12.8f })
                Box("Balcony pillar", V(11.3f, 1.25f, z), V(.35f, 2.5f, .38f));
            MakeExit(10, 13, 2.8f, 2.15f);

            Loose(new Vector2(3.3f, -11.7f), new Vector2(.3f, -11.2f), new Vector2(-4.1f, -11.6f),
                new Vector2(-9.7f, -7), new Vector2(-11.8f, -.9f), new Vector2(-7.3f, .3f),
                new Vector2(-8, 6.1f), new Vector2(-3.2f, 9.4f), new Vector2(2.9f, 7.2f),
                new Vector2(2.4f, 2.2f), new Vector2(4.6f, -2.8f), new Vector2(6.9f, -5.5f),
                new Vector2(8.2f, -7.7f), new Vector2(6.4f, 5), new Vector2(.9f, -4.2f));
        }

        private void BuildCollapsibleCrossing()
        {
            crossingArch = Group(architecture, "Lower crossing and timber canopy");
            crossingPieces = new[]
            {
                CrossingBlock("West support", V(-1.52f, 1.48f, 11.6f), V(.30f, 2.96f, .34f),
                    V(-1.76f, .37f, 12.11f), V(59, -28, -39), .3f, 1.14f),
                CrossingBlock("East support", V(1.52f, 1.48f, 11.6f), V(.30f, 2.96f, .34f),
                    V(1.32f, .48f, 11.64f), V(-57, 12, 38), .51f, 1.32f),
                CrossingBlock("Front crossbeam", V(0, 3.06f, 11.59f), V(3.48f, .41f, .53f),
                    V(-.14f, .29f, 12.08f), V(13, 18, 10), .46f, 1.4f, true),
                CrossingBlock("West canopy rafter", V(-.91f, 3.32f, 12.7f), V(.23f, .23f, 2.57f),
                    V(-1.22f, .52f, 11.91f), V(34, -33, -8), .58f, 1.25f),
                CrossingBlock("East canopy rafter", V(.91f, 3.32f, 12.7f), V(.23f, .23f, 2.57f),
                    V(.82f, .61f, 12.21f), V(-27, 36, -23), .62f, 1.53f),
                CrossingBlock("West crown stone", V(-.89f, 4.19f, 14.05f), V(1.71f, 1.14f, .6f),
                    V(-.9f, 1.40f, 13.28f), V(17, -16, 20), .75f, 1.62f),
                CrossingBlock("East crown stone", V(.89f, 4.22f, 14.05f), V(1.71f, 1.18f, .6f),
                    V(1.05f, .59f, 12.97f), V(-16, 27, -36), .86f, 1.73f),
                CrossingBlock("Main lower lintel", V(0, 3.48f, 13.92f), V(3.72f, .97f, .94f),
                    V(0, .76f, 13.66f), V(7, -6, 5), .74f, 1.96f, true),
            };
        }

        private CrossingPiece CrossingBlock(string name, Vector3 standing, Vector3 scale, Vector3 fallen,
            Vector3 rotation, float delay, float duration, bool blocksPath = false)
        {
            var block = Box(name, standing, scale, blocksPath, crossingArch);
            var collider = blocksPath ? block.GetComponent<Collider>() : null;
            if (collider) collider.enabled = false;
            return new CrossingPiece
            {
                Transform = block.transform, Collider = collider, Standing = standing, Fallen = fallen,
                FallenRotation = Quaternion.Euler(rotation), Delay = delay, Duration = duration
            };
        }

        private void BuildBallroom()
        {
            Spawn = World(0, -16);
            CreatureSpawn = World(-9.7f, 10.4f);
            Patrol = WorldPoints(new Vector2(-9.7f, 10.4f), new Vector2(-9.6f, 1.1f),
                new Vector2(-6.8f, -12.2f), new Vector2(6.4f, -12.2f), new Vector2(9.7f, -3.2f),
                new Vector2(9.7f, 12.8f), new Vector2(0, 16.2f));
            QuietZones = new[] { new Bounds(World(-12.95f, 5.7f, 1.25f), V(3.8f, 3.2f, 6.4f)),
                                 new Bounds(World(12.95f, 5.7f, 1.25f), V(3.8f, 3.2f, 6.4f)) };
            navigationBounds = new Bounds(World(0, 0, 2.5f), V(32, 12, 41));
            Floor("Resonant ballroom floor", V(0, -.16f, 0), V(30, .32f, 38));
            SideWalls(15.1f, -18.9f, 18.9f, 5.7f);
            WallSpan("South ballroom wall", -15.2f, 15.2f, -18.9f, 5.7f);
            WallSpan("Stage wall west", -15.2f, -1.45f, 18.9f, 5.7f);
            WallSpan("Stage wall east", 1.45f, 15.2f, 18.9f, 5.7f);
            Box("Backstage lintel", V(0, 4.35f, 18.9f), V(2.9f, 2.7f, .36f));
            DoorTrim("Ballroom entry", 0, -18.68f, 0, 3.4f, false);
            DoorTrim("Backstage exit", 0, 18.64f, 0, 2.8f, true);
            MakeExit(0, 17.6f, 0, 2.25f);

            Floor("Parquet border", V(0, -.074f, -3), V(15.3f, .18f, 25.2f), false);
            Box("Entrance service table", V(0, .46f, -12.3f), V(3.4f, .92f, 1.05f));
            Box("Service table top", V(0, .96f, -12.3f), V(3.55f, .13f, 1.15f), false);
            PrototypeWorld.Primitive("Round display pedestal", PrimitiveType.Cylinder, architecture,
                V(2.45f, .54f, -8.45f), V(2.08f, .54f, 2.08f), surface, true);
            Box("Folding screen in the dance lane", V(-2.8f, .88f, -4.65f), V(2.4f, 1.76f, .58f));
            Box("Screen cap rail", V(-2.8f, 1.77f, -4.65f), V(2.55f, .13f, .68f), false);
            Box("Center banquet table", V(.45f, .5f, .05f), V(2.55f, 1f, 1.38f));
            Box("Banquet table top", V(.45f, 1f, .05f), V(2.71f, .14f, 1.55f), false);
            for (int i = -1; i <= 1; i++)
                Box("Raised ceiling beam", V(0, 5.35f, i * 9.2f), V(29.7f, .36f, .55f), false);
            foreach (float x in new[] { -7.7f, 7.7f })
            {
                foreach (float z in new[] { -9.6f, -2.2f, 5.2f, 11.1f })
                    Column("Ballroom column", x, z, 5);
                float side = Mathf.Sign(x);
                for (int i = 0; i < 4; i++)
                    Box("Drape fold", V(side * (10.68f + .12f * (i % 2)), 1.28f, 3.9f + i * .95f), V(.24f, 2.56f, 1.04f), false);
                Box("Alcove curtain backing", V(side * 10.8f, 1.28f, 5.35f), V(.32f, 2.56f, 4.05f));
                Box("Padded alcove bench", V(side * 14.2f, .43f, 6.1f), V(1.0f, .86f, 3.1f));
                Box("Perimeter seating", V(side * 12.6f, .49f, -6.1f), V(1.7f, .98f, 3.7f));
                Box("Low table", V(side * 11.4f, .42f, -11), V(1.7f, .84f, 2.3f));
            }
            Floor("Raised stage", V(0, .25f, 12.9f), V(10.5f, .5f, 4.1f));
            Floor("Stage lower tread", V(0, .08f, 10.45f), V(3.05f, .16f, .8f));
            Floor("Stage upper tread", V(0, .16f, 10.75f), V(3.05f, .32f, .58f));
            Box("Grand piano body", V(-1, 1.24f, 12.76f), V(2.85f, 1.34f, 1.68f));
            Box("Piano keyboard", V(-1, 1.16f, 11.88f), V(2.93f, .14f, .45f), false);
            foreach (float x in new[] { -5.32f, 5.32f })
                Box("Stage proscenium", V(x, 1.7f, 14.6f), V(.38f, 3.4f, .62f));
            Box("Stage top rail", V(0, 3.44f, 14.6f), V(11f, .31f, .58f), false);
            Loose(new Vector2(.65f, -14.4f), new Vector2(-1.5f, -13.7f), new Vector2(-5.2f, -11.8f),
                new Vector2(4.8f, -11.1f), new Vector2(-3.7f, -6.8f), new Vector2(2.6f, -6.7f),
                new Vector2(-.3f, -2.5f), new Vector2(5.2f, 1.6f), new Vector2(-7.8f, 1.2f),
                new Vector2(-12.5f, 4.8f), new Vector2(12.4f, 7.8f), new Vector2(-4.9f, 6.8f),
                new Vector2(3.3f, 8.3f), new Vector2(-7.3f, 14f), new Vector2(7, 14.8f),
                new Vector2(11.8f, 12.9f), new Vector2(2, 16.2f));
        }

        private void BuildMaze()
        {
            Spawn = World(0, -12);
            CreatureSpawn = World(0, 9.5f);
            SecondCreatureSpawn = World(-5.2f, 6.4f);
            FinalStaging = World(0, 4.1f);
            PrimaryLure = World(-5.6f, 6.9f);
            SecondaryLure = World(5.5f, 7.6f);
            // The first leg leads north; the remainder stays beyond the entry walls.
            Patrol = WorldPoints(new Vector2(-5.2f, 6.4f), new Vector2(-5.2f, 9.6f),
                new Vector2(-7.7f, 11), new Vector2(-9.4f, 7.25f), new Vector2(-8.25f, 4.55f),
                new Vector2(-8.2f, .75f), new Vector2(-7.6f, 1.2f), new Vector2(-7.9f, 6.7f));
            navigationBounds = new Bounds(World(0, 0, 2), V(26, 10, 29));
            Floor("Service maze floor", V(0, -.16f, 0), V(24, .32f, 26.6f));
            SideWalls(12.1f, -13.2f, 13.2f, 3.2f);
            WallSpan("South service wall", -12.2f, 12.2f, -13.3f, 3.2f);
            WallSpan("North service wall, left", -12.2f, -1.36f, 13.3f, 3.2f);
            WallSpan("North service wall, right", 1.36f, 12.2f, 13.3f, 3.2f);
            Box("Exit lintel", V(0, 3.01f, 13.3f), V(2.72f, .38f, .38f));
            DoorTrim("Final service exit", 0, 13.05f, 0, 2.65f, true);
            MakeExit(0, 12f, 0, 2.15f);

            WallSpan("South west retaining wall", -12, -6.1f, -8.2f, 2.6f);
            WallSpan("South center retaining wall", -4, 1.8f, -8.2f, 2.6f);
            WallSpan("South east retaining wall", 4.5f, 12, -8.2f, 2.6f);
            Box("West relay partition", V(-4.5f, 1.1f, -5.9f), V(.42f, 2.2f, 3.3f));
            Box("East relay partition", V(5.1f, 1.1f, -5.18f), V(.42f, 2.2f, 3.7f));
            WallSpan("West mid wall", -12, -8.6f, -1.6f, 2.6f);
            WallSpan("Center west relay wall", -5.8f, -1.6f, -1.6f, 2.6f);
            WallSpan("Center east relay wall", 1.5f, 7.6f, -1.6f, 2.6f);
            WallSpan("East mid wall", 10, 12, -1.6f, 2.6f);
            WallSpan("Gallery wall west", -12, -9, 3.3f, 2.8f);
            WallSpan("Gallery wall inside left", -6.75f, -2.3f, 3.3f, 2.8f);
            WallSpan("Gallery wall inside right", 2.35f, 7.2f, 3.3f, 2.8f);
            WallSpan("Gallery wall east", 9.4f, 12, 3.3f, 2.8f);
            Box("West screen before the exit", V(-2.3f, 1.06f, 3.85f), V(.35f, 2.12f, 1.12f));
            Box("East screen before the exit", V(2.35f, 1.06f, 3.85f), V(.35f, 2.12f, 1.12f));

            Box("West cistern wall", V(-7.2f, .81f, 7.9f), V(.4f, 1.62f, 5.2f));
            for (int i = 0; i < 3; i++)
                PrototypeWorld.Rod("Cistern pipe", architecture, V(-6.93f, .56f + i * .32f, 5.85f),
                    V(-6.93f, .56f + i * .32f, 9.9f), .085f, surface, 0);
            Box("East wire enclosure backing", V(7.15f, .97f, 8.1f), V(.44f, 1.94f, 5f));
            for (int i = 0; i < 5; i++)
                Box("Enclosure rail", V(6.9f, 1.14f, 6.32f + i * .83f), V(.18f, 1.82f, .12f), false);
            Floor("Paired throw preparation mark", V(0, -.082f, 4.1f), V(2.7f, .2f, 1.32f), false);
            Box("West pump housing", V(-6.97f, .55f, -5.25f), V(1.32f, 1.1f, 1.2f));
            Box("East storage crate", V(8.3f, .51f, -5.86f), V(1.2f, 1.02f, 1.55f));
            Column("Recovery alcove pier", -10.65f, 1.05f, 2.1f);
            Column("East turn pier", 10.45f, 1.25f, 2.1f);
            Loose(new Vector2(.88f, -11.5f), new Vector2(-3.46f, -10), new Vector2(-5.3f, -6.65f),
                new Vector2(-8.7f, -3.4f), new Vector2(6.9f, -5), new Vector2(9.4f, -3.2f),
                new Vector2(-9.8f, 1.9f), new Vector2(9.45f, 1.5f), new Vector2(-.8f, 1.7f),
                new Vector2(-.62f, 4.46f), new Vector2(.6f, 4.5f), new Vector2(-4.4f, 5.9f), new Vector2(4.15f, 5.71f));
        }

        public void PrepareMazeScan(GameSession session)
        {
            MazeOverview = gameObject.AddComponent<MazeScan>();
            MazeOverview.Initialize(session, architecture, navigationBounds);
        }

        private void Ramp(Vector3 bottom, Vector3 top, float width)
        {
            Vector3 along = (top - bottom).normalized;
            Quaternion slope = Quaternion.FromToRotation(Vector3.forward, along);
            Vector3 normal = slope * Vector3.up;
            Vector3 middle = (bottom + top) * .5f;
            float length = Vector3.Distance(bottom, top);
            var ramp = Floor("Accessible service stair slope", middle - normal * .14f, V(width, .28f, length));
            ramp.transform.localRotation = slope;
            foreach (float x in new[] { bottom.x - width / 2, bottom.x + width / 2 })
            {
                var rail = Box("Service stair handrail", V(x, middle.y + .41f, middle.z), V(.14f, .82f, length));
                rail.transform.localRotation = slope;
            }
            for (int i = 1; i <= 13; i++)
            {
                Vector3 tread = Vector3.Lerp(bottom, top, i / 14f) + normal * .026f;
                Box("Service stair nosing", tread, V(width - .18f, .042f, .075f), false);
            }
        }

        private void MakeExit(float x, float z, float baseHeight, float width)
        {
            Exit = Group(transform, "Exit threshold");
            Exit.localPosition = V(x, baseHeight, z);
            ExitThreshold = Exit.gameObject.AddComponent<BoxCollider>();
            ExitThreshold.isTrigger = true;
            ExitThreshold.center = V(0, 1, 0);
            ExitThreshold.size = V(width, 2.2f, 1.6f);
        }

        private void DoorTrim(string name, float x, float z, float floor, float width, bool door)
        {
            foreach (float side in new[] { -1f, 1f })
                Box(name + " stone jamb", V(x + side * (width / 2), floor + 1.39f, z), V(.16f, 2.78f, .28f));
            Box(name + " top lintel", V(x, floor + 2.86f, z), V(width + .17f, .17f, .29f));
            if (!door) return;
            Box(name + " door leaf", V(x, floor + 1.32f, z + .085f), V(width - .16f, 2.64f, .14f));
            Box(name + " push bar", V(x, floor + .98f, z - .03f), V(width * .7f, .08f, .12f), false);
        }

        private void Planter(string name, float x, float z, float width, float length)
        {
            Box(name, V(x, .46f, z), V(width, .92f, length));
            Box(name + " stone rim", V(x, .88f, z), V(width + .12f, .13f, length + .12f), false);
        }

        private void Column(string name, float x, float z, float height)
        {
            Box(name, V(x, height / 2, z), V(.56f, height, .56f));
            Box(name + " base", V(x, .18f, z), V(.8f, .36f, .8f));
            Box(name + " cap", V(x, height - .12f, z), V(.8f, .29f, .8f), false);
        }

        private void SideWalls(float x, float south, float north, float height)
        {
            foreach (float side in new[] { -1f, 1f })
                Box(side < 0 ? "West boundary" : "East boundary", V(side * x, height / 2, (north + south) / 2),
                    V(.38f, height, north - south + .35f));
        }

        private void WallSpan(string name, float left, float right, float z, float height)
            => Box(name, V((left + right) / 2, height / 2, z), V(right - left, height, .38f));

        private void Loose(params Vector2[] points)
        {
            for (int i = 0; i < points.Length; ++i)
                PrototypeWorld.CreateThrowable(props, V(points[i].x, 0, points[i].y), i % 3, surface);
        }

        private GameObject Floor(string name, Vector3 position, Vector3 scale, bool collision = true)
        {
            var floor = Box(name, position, scale, collision);
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_Floor", 1);
            floor.GetComponent<Renderer>().SetPropertyBlock(properties);
            return floor;
        }

        private GameObject Box(string name, Vector3 position, Vector3 scale, bool collision = true, Transform parent = null)
            => PrototypeWorld.Primitive(name, PrimitiveType.Cube, parent != null ? parent : architecture,
                position, scale, surface, collision);

        private Vector3 World(float x, float z, float y = 0) => transform.TransformPoint(V(x, y, z));

        private Vector3[] WorldPoints(params Vector2[] points)
        {
            var world = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) world[i] = World(points[i].x, points[i].y);
            return world;
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    }
}
