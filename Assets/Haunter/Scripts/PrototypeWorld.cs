using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    /// <summary>An editable, authored room; only the navigation mesh is rebuilt at runtime.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PrototypeWorld : MonoBehaviour
    {
        [SerializeField] private Transform staticGeometry;
        [SerializeField] private Renderer floorRenderer;
        [SerializeField] private BoxCollider exitThreshold;
        [SerializeField] private Transform playerReference;
        private NavMeshData navigationData;
        private NavMeshDataInstance navigationInstance;
        public bool NavMeshReady { get; private set; }

        private void Awake()
        {
            if (staticGeometry == null) staticGeometry = transform.Find("Static geometry");
            if (floorRenderer != null) ApplyFloorMask(floorRenderer);
            if (staticGeometry != null) BuildNavigation();
        }

        private void FixedUpdate()
        {
            if (exitThreshold == null || playerReference == null || GameSession.Instance == null) return;
            if (GameSession.Instance.State == SessionState.Playing &&
                exitThreshold.bounds.Contains(playerReference.position + Vector3.up * .8f))
                GameSession.Instance.Escape();
        }

        private static void ApplyFloorMask(Renderer floor)
        {
            var properties = new MaterialPropertyBlock();
            floor.GetPropertyBlock(properties);
            properties.SetFloat("_Floor", 1);
            floor.SetPropertyBlock(properties);
        }

        public void BuildNavigation()
        {
            if (NavMeshReady || staticGeometry == null) return;
            Physics.SyncTransforms();
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(staticGeometry, 1, NavMeshCollectGeometry.PhysicsColliders,
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
            navigationData = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, new Vector3(26, 12, 34)), Vector3.zero, Quaternion.identity);
            if (navigationData == null)
            {
                Debug.LogError("Haunter: could not build the room's navigation mesh.");
                return;
            }
            navigationInstance = NavMesh.AddNavMeshData(navigationData);
            NavMeshReady = navigationInstance.valid;
        }

        private void OnDestroy()
        {
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigationData != null) Destroy(navigationData);
        }

        public static void Build(Material material)
        {
            var root = new GameObject("HAUNTER IN THE DARK");
            var architecture = new GameObject("Static geometry").transform;
            architecture.SetParent(root.transform, false);
            var props = new GameObject("Loose objects — pick up and throw").transform;
            props.SetParent(root.transform, false);

            RenderSettings.skybox = null;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            var floor = Box("Flagstone floor", architecture, new Vector3(0, -.15f, 0),
                new Vector3(22, .3f, 30), material);
            ApplyFloorMask(floor.GetComponent<Renderer>());
            Box("West wall", architecture, new Vector3(-11.2f, 2.1f, 0), new Vector3(.4f, 4.2f, 30.4f), material);
            Box("East wall", architecture, new Vector3(11.2f, 2.1f, 0), new Vector3(.4f, 4.2f, 30.4f), material);
            Box("South wall", architecture, new Vector3(0, 2.1f, -15.2f), new Vector3(22.8f, 4.2f, .4f), material);
            Box("North wall left", architecture, new Vector3(-6.2f, 2.1f, 15.2f), new Vector3(9.6f, 4.2f, .4f), material);
            Box("North wall right", architecture, new Vector3(6.2f, 2.1f, 15.2f), new Vector3(9.6f, 4.2f, .4f), material);
            Box("Door lintel", architecture, new Vector3(0, 3.7f, 15.2f), new Vector3(2.8f, 1f, .4f), material);

            // High beams and perimeter details make waves reveal a coherent space overhead.
            foreach (float z in new[] { -10f, -3f, 4f, 11f })
            {
                Box("Ceiling crossbeam", architecture, new Vector3(0, 3.9f, z), new Vector3(22, .25f, .22f), material, false);
                foreach (float x in new[] { -10.7f, 10.7f })
                {
                    Box("Wall pilaster", architecture, new Vector3(x, 1.85f, z), new Vector3(.35f, 3.7f, .45f), material);
                    Box("Pilaster foot", architecture, new Vector3(x, .17f, z), new Vector3(.58f, .34f, .65f), material);
                }
            }
            foreach (float x in new[] { -10.95f, 10.95f })
                Box("Low wall moulding", architecture, new Vector3(x, .16f, 0), new Vector3(.08f, .12f, 30), material, false);

            for (int side = -1; side <= 1; side += 2)
            {
                Shelf(architecture, new Vector3(side * 8.35f, 0, -6.2f), side * 7f, material, 11 + side);
                Shelf(architecture, new Vector3(side * 8.35f, 0, .2f), side * -4f, material, 22 + side);
                Shelf(architecture, new Vector3(side * 8.35f, 0, 7.4f), side * 3f, material, 33 + side);
            }

            Table(architecture, new Vector3(-1.65f, 0, -4.5f), -8, new Vector2(4.4f, 1.8f), material);
            Table(architecture, new Vector3(2.25f, 0, 3.3f), 10, new Vector2(4.2f, 1.8f), material);
            Table(architecture, new Vector3(-5.4f, 0, 10), -4, new Vector2(2.7f, 1.4f), material);
            Chair(architecture, new Vector3(-2.5f, 0, -6.1f), 172, material);
            Chair(architecture, new Vector3(.05f, 0, -3f), -22, material);
            Chair(architecture, new Vector3(3.2f, 0, 5f), 13, material);
            Chair(architecture, new Vector3(-5.5f, 0, 11.2f), 4, material);

            Crate(architecture, new Vector3(-5.15f, .45f, -.3f), new Vector3(1.3f, .9f, 1.1f), 12, material);
            Crate(architecture, new Vector3(-5.45f, 1.12f, -.3f), new Vector3(.75f, .44f, .85f), -9, material);
            Crate(architecture, new Vector3(5.7f, .45f, -8.4f), new Vector3(1.3f, .9f, 1.1f), -7, material);
            Crate(architecture, new Vector3(6.8f, .4f, -9.2f), new Vector3(.9f, .8f, .9f), 16, material);

            // A safe first pickup is directly in front of the opening position.
            CreateThrowable(props, new Vector3(-.55f, .45f, -10.1f), 0, material);
            CreateThrowable(props, new Vector3(.7f, .55f, -9.7f), 1, material);
            CreateThrowable(props, new Vector3(1.45f, .3f, -11.4f), 2, material);
            var propPositions = new[]
            {
                new Vector3(-3.5f,.35f,-10.8f), new Vector3(3.4f,.35f,-9.8f),
                new Vector3(-5.8f,.35f,-9.4f), new Vector3(5.1f,.35f,-6.3f),
                new Vector3(2.1f,.35f,-6.8f), new Vector3(-4.5f,.35f,-6.8f),
                new Vector3(-1.9f,1.25f,-4.3f), new Vector3(-.3f,1.3f,-4.8f),
                new Vector3(-6.6f,.4f,-3.1f), new Vector3(5.9f,.35f,-2.9f),
                new Vector3(2.6f,.35f,-1.3f), new Vector3(.3f,.35f,.6f),
                new Vector3(-3.1f,.35f,2.4f), new Vector3(-5.8f,.35f,3.1f),
                new Vector3(5.8f,.35f,2.2f), new Vector3(1.3f,1.3f,3.1f),
                new Vector3(3.7f,1.3f,3.4f), new Vector3(-1.1f,.4f,5.6f),
                new Vector3(5.8f,.35f,6.8f), new Vector3(-5.1f,.35f,6.3f),
                new Vector3(2.5f,.35f,8.7f), new Vector3(-1.7f,.35f,8.1f),
                new Vector3(-5.8f,1.3f,9.9f), new Vector3(-4.8f,1.25f,10.1f),
                new Vector3(6.4f,.35f,11.1f), new Vector3(3.4f,.35f,12.1f),
                new Vector3(-2.4f,.35f,12.2f), new Vector3(-7.1f,.4f,12.6f),
                new Vector3(1.1f,.35f,10.5f)
            };
            for (int i = 0; i < propPositions.Length; i++) CreateThrowable(props, propPositions[i], i % 3, material);

            var exit = new GameObject("Exit — reach this door");
            exit.transform.SetParent(root.transform, false);
            exit.transform.position = new Vector3(0, 0, 13.7f);
            Box("Exit left jamb", architecture, new Vector3(-1.35f, 1.55f, 14.5f), new Vector3(.15f, 3.1f, .35f), material);
            Box("Exit right jamb", architecture, new Vector3(1.35f, 1.55f, 14.5f), new Vector3(.15f, 3.1f, .35f), material);
            Box("Exit header", architecture, new Vector3(0, 3.05f, 14.5f), new Vector3(2.85f, .15f, .35f), material);
            Box("Exit door", architecture, new Vector3(0, 1.45f, 14.7f), new Vector3(2.5f, 2.9f, .16f), material);
            Box("Exit inset panel", architecture, new Vector3(0, 1.6f, 14.59f), new Vector3(2.05f, 2.1f, .06f), material, false);
            Box("Exit push bar", architecture, new Vector3(0, 1.13f, 14.43f), new Vector3(1.8f, .075f, .12f), material, false);
            var trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0, 1f, 0);
            trigger.size = new Vector3(2.3f, 2.2f, 1.6f);

            var playerObject = new GameObject("Player");
            playerObject.transform.SetParent(root.transform, false);
            playerObject.transform.position = new Vector3(0, .03f, -12);
            playerObject.layer = 9;
            var controller = playerObject.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.center = new Vector3(0, .9f, 0);
            controller.radius = .3f;
            controller.stepOffset = .22f;
            controller.skinWidth = .035f;
            var cameraObject = new GameObject("First person camera");
            cameraObject.tag = "MainCamera";
            cameraObject.layer = 9;
            cameraObject.transform.SetParent(playerObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.62f, 0);
            var view = cameraObject.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            view.fieldOfView = 77;
            view.nearClipPlane = .04f;
            view.farClipPlane = 55;
            view.allowHDR = false;
            view.allowMSAA = false;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<EchoRenderer>();
            var player = playerObject.AddComponent<PlayerController>();
            player.Initialize(view);

            var cane = new GameObject("Cane").transform;
            cane.SetParent(cameraObject.transform, false);
            cane.localPosition = new Vector3(.38f, -.4f, .65f);
            Rod("Cane shaft", cane, Vector3.zero, new Vector3(.07f, -1.02f, .72f), .018f, material, 9);
            Rod("Cane grip", cane, new Vector3(-.06f, .035f, -.04f), new Vector3(.10f, .035f, -.04f), .03f, material, 9);
            var tip = Primitive("Cane tip", PrimitiveType.Sphere, cane, new Vector3(.07f, -1.02f, .72f),
                new Vector3(.055f, .07f, .055f), material, false);
            tip.layer = 9;
            EchoMaterial.SetPersistent(cane.gameObject, true);
            player.SetCane(cane);

            var services = new GameObject("Game services");
            services.transform.SetParent(root.transform, false);
            services.AddComponent<NoiseSystem>();
            var session = services.AddComponent<GameSession>();
            session.Player = player;
            session.Exit = exit.transform;
            var world = root.AddComponent<PrototypeWorld>();
            world.staticGeometry = architecture;
            world.floorRenderer = floor.GetComponent<Renderer>();
            world.exitThreshold = trigger;
            world.playerReference = player.transform;
            if (Application.isPlaying) world.BuildNavigation();

            var creatureObject = new GameObject("The listener — four paws");
            creatureObject.layer = 10;
            creatureObject.transform.SetParent(root.transform, false);
            creatureObject.transform.position = exit.transform.position + new Vector3(0, .05f, -2.2f);
            creatureObject.transform.rotation = Quaternion.Euler(0, 180, 0);
            var creature = creatureObject.AddComponent<ListenerCreature>();
            creature.Initialize(player.transform, material);
            session.Creature = creature;
        }

        private static void Shelf(Transform parent, Vector3 position, float angle, Material material, int seed)
        {
            var shelf = new GameObject("Archive shelving").transform;
            shelf.SetParent(parent, false);
            shelf.localPosition = position;
            shelf.localRotation = Quaternion.Euler(0, angle, 0);
            var blocker = shelf.gameObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0, 1.4f, 0);
            blocker.size = new Vector3(3.4f, 2.8f, .95f);
            foreach (float x in new[] { -1.65f, 1.65f })
                Box("Shelf upright", shelf, new Vector3(x, 1.4f, 0), new Vector3(.1f, 2.8f, .9f), material, false);
            var random = new System.Random(seed);
            foreach (float y in new[] { .15f, .9f, 1.65f, 2.4f, 2.8f })
            {
                Box("Shelf board", shelf, new Vector3(0, y, 0), new Vector3(3.4f, .075f, .95f), material, false);
                if (y > 2.5f) continue;
                float x = -1.48f;
                while (x < 1.45f)
                {
                    float width = .10f + (float)random.NextDouble() * .18f;
                    float height = .35f + (float)random.NextDouble() * .28f;
                    if (random.NextDouble() > .18)
                    {
                        var volume = Box("Dusty volume", shelf, new Vector3(x + width * .5f, y + height * .5f + .04f, -.12f),
                            new Vector3(width, height, .50f + .15f * (float)random.NextDouble()), material, false);
                        volume.transform.localRotation = Quaternion.Euler(0, 0, random.Next(-3, 4));
                    }
                    x += width + .045f;
                }
            }
        }

        private static void Table(Transform parent, Vector3 position, float angle, Vector2 size, Material material)
        {
            var table = new GameObject("Reading table").transform;
            table.SetParent(parent, false);
            table.localPosition = position;
            table.localRotation = Quaternion.Euler(0, angle, 0);
            Box("Table top", table, new Vector3(0, .96f, 0), new Vector3(size.x, .12f, size.y), material);
            foreach (float x in new[] { -size.x * .43f, size.x * .43f })
                foreach (float z in new[] { -size.y * .38f, size.y * .38f })
                    Box("Table leg", table, new Vector3(x, .46f, z), new Vector3(.14f, .92f, .14f), material);
            Box("Table apron", table, new Vector3(0, .8f, 0), new Vector3(size.x * .93f, .23f, size.y * .87f), material, false);
        }

        private static void Chair(Transform parent, Vector3 position, float angle, Material material)
        {
            var chair = new GameObject("Reading chair").transform;
            chair.SetParent(parent, false);
            chair.localPosition = position;
            chair.localRotation = Quaternion.Euler(0, angle, 0);
            var blocker = chair.gameObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0, .53f, 0);
            blocker.size = new Vector3(.64f, 1.06f, .67f);
            Box("Chair seat", chair, new Vector3(0, .48f, 0), new Vector3(.62f, .09f, .6f), material, false);
            foreach (float x in new[] { -.24f, .24f })
            {
                Box("Chair front leg", chair, new Vector3(x, .24f, .22f), new Vector3(.07f, .48f, .07f), material, false);
                Box("Chair back leg", chair, new Vector3(x, .52f, -.25f), new Vector3(.07f, 1.04f, .07f), material, false);
            }
            Box("Chair back", chair, new Vector3(0, .91f, -.25f), new Vector3(.6f, .22f, .07f), material, false);
        }

        private static void Crate(Transform parent, Vector3 position, Vector3 size, float angle, Material material)
        {
            var crate = Box("Storage crate", parent, position, size, material);
            crate.transform.localRotation = Quaternion.Euler(0, angle, 0);
            foreach (float x in new[] { -.32f, .32f })
                Box("Crate band", crate.transform, new Vector3(x, 0, -.507f), new Vector3(.075f, 1.02f, .025f), material, false);
        }

        private static void CreateThrowable(Transform parent, Vector3 position, int type, Material material)
        {
            string displayName = type == 0 ? "Tin can" : type == 1 ? "Glass bottle" : "Loose stone";
            // Place resting props at contact height so starting the game does not trigger
            // a room-wide cascade of artificial impact sounds as every prop settles.
            position.y = (position.y > 1.1f ? 1.02f : 0f) + (type == 1 ? .25f : .16f) + .006f;
            var prop = new GameObject(displayName);
            prop.layer = 8;
            prop.transform.SetParent(parent, false);
            prop.transform.localPosition = position;
            prop.transform.localRotation = Quaternion.Euler(0, position.x * 53f, 0);
            Collider collider;
            if (type == 0)
            {
                Primitive("Can", PrimitiveType.Cylinder, prop.transform, Vector3.zero, new Vector3(.23f, .15f, .23f), material, false);
                Primitive("Top rim", PrimitiveType.Cylinder, prop.transform, new Vector3(0, .142f, 0), new Vector3(.247f, .012f, .247f), material, false);
                Primitive("Bottom rim", PrimitiveType.Cylinder, prop.transform, new Vector3(0, -.142f, 0), new Vector3(.247f, .012f, .247f), material, false);
                var capsule = prop.AddComponent<CapsuleCollider>();
                capsule.radius = .12f;
                capsule.height = .32f;
                collider = capsule;
            }
            else if (type == 1)
            {
                Primitive("Bottle body", PrimitiveType.Cylinder, prop.transform, new Vector3(0, -.04f, 0), new Vector3(.19f, .17f, .19f), material, false);
                Primitive("Bottle shoulder", PrimitiveType.Sphere, prop.transform, new Vector3(0, .13f, 0), new Vector3(.19f, .13f, .19f), material, false);
                Primitive("Bottle neck", PrimitiveType.Cylinder, prop.transform, new Vector3(0, .23f, 0), new Vector3(.075f, .085f, .075f), material, false);
                Primitive("Bottle mouth", PrimitiveType.Cylinder, prop.transform, new Vector3(0, .31f, 0), new Vector3(.095f, .018f, .095f), material, false);
                var capsule = prop.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0, .04f, 0);
                capsule.radius = .105f;
                capsule.height = .58f;
                collider = capsule;
            }
            else
            {
                var stone = Primitive("Stone", PrimitiveType.Sphere, prop.transform, Vector3.zero, new Vector3(.34f, .22f, .28f), material, false);
                stone.transform.localRotation = Quaternion.Euler(12, 0, 18);
                var sphere = prop.AddComponent<SphereCollider>();
                sphere.radius = .16f;
                collider = sphere;
            }
            foreach (Transform child in prop.transform) child.gameObject.layer = 8;
            var body = prop.AddComponent<Rigidbody>();
            body.mass = type == 2 ? 1.0f : .4f;
            body.drag = .06f;
            body.angularDrag = .12f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var throwable = prop.AddComponent<ThrowableObject>();
            throwable.DisplayName = displayName;
            collider.contactOffset = .01f;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            return Primitive(name, PrimitiveType.Cube, parent, position, scale, material, collider);
        }

        internal static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision)
            {
                var collider = obj.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }
            return obj;
        }

        internal static Transform Rod(string name, Transform parent, Vector3 start, Vector3 end, float radius, Material material, int layer)
        {
            var obj = Primitive(name, PrimitiveType.Cylinder, parent, (start + end) * .5f,
                new Vector3(radius * 2, Vector3.Distance(start, end) * .5f, radius * 2), material, false);
            obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
            obj.layer = layer;
            return obj.transform;
        }
    }

}
