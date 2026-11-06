// The package's Avatars sample (Packages/com.hawkes.net/Samples~/Avatars), as a scene Hawkes Studio builds: Samples~ is
// not imported, so this is a copy with the demo project ("netdemo", test rooms) filled in. HawkesNetDemoBuild makes the
// scene and builds it to WebGL. Keep it in step with the sample when the sample changes.
using UnityEngine;

namespace Hawkes.Demo
{
    /// <summary>
    /// The smallest multiplayer scene: a capsule you walk with WASD (or the arrow keys), a camera behind it, and a
    /// capsule for every other player in the area's shared room, through HawkesNetAvatars. Put it on an empty
    /// GameObject in an empty scene, set Project, build for WebGL and open two tabs.
    /// </summary>
    public sealed class HawkesNetDemo : MonoBehaviour
    {
        [Tooltip("Your account's project (usr_...), from https://net.hawkesgen.ai.")]
        public string Project = "netdemo";
        [Tooltip("Your game's own host names (mygame.com): there the rooms are live, anywhere else test rooms.")]
        public string[] LiveHosts = new string[0];
        public string Area = "unity-demo";
        public float Speed = 5f;
        [Tooltip("Half the side of the square arena (m): the ground and its edge walls are at plus and minus this.")]
        public float ArenaHalf = 12f;

        Transform player;
        HawkesNetAvatars avatars;

        // CreatePrimitive gives the built-in Default-Material (the Standard shader). Nothing in this scene references
        // that shader, so a WebGL build strips it and every primitive renders as Unity's error magenta. So each object
        // gets a material from a shader that is looked up by name (kept by Always Included Shaders: HawkesNetDemoBuild
        // adds Standard; in your own project add the shader your render pipeline uses), with fallbacks.
        static Material MakeMaterial(Color color)
        {
            Shader shader = null;
            foreach (var n in new[] { "Standard", "Universal Render Pipeline/Lit", "Sprites/Default" })
                if (!shader) shader = Shader.Find(n);
            var m = new Material(shader);
            m.color = color;
            return m;
        }

        void Awake()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            // A Plane is 10 m on a side at scale 1.
            ground.transform.localScale = new Vector3(ArenaHalf * 0.2f, 1f, ArenaHalf * 0.2f);
            // CreatePrimitive adds a collider inside the engine, so a build that strips unused engine code (WebGL) drops
            // the physics module unless managed code names these types ("Can't add component because class
            // 'MeshCollider' doesn't exist!"). Naming them here keeps them; the scene moves by transform, so nothing
            // needs the colliders.
            Destroy(ground.GetComponent<MeshCollider>());
            ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.35f, 0.55f, 0.35f));
            var playerCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(playerCapsule.GetComponent<CapsuleCollider>());
            player = playerCapsule.transform;
            player.position = new Vector3(Random.Range(-4f, 4f), 1f, Random.Range(-4f, 4f));
            player.GetComponent<Renderer>().sharedMaterial = MakeMaterial(Color.HSVToRGB(Random.value, 0.6f, 0.9f));

            // Everyone else: a grey capsule, kept inactive as the template.
            var remote = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            remote.name = "Remote template";
            Destroy(remote.GetComponent<CapsuleCollider>());
            remote.GetComponent<Renderer>().sharedMaterial = MakeMaterial(Color.gray);
            remote.SetActive(false);

            // Edge walls: the player is kept inside them (Update), so the arena is bounded whatever the input.
            var wallMaterial = MakeMaterial(new Color(0.55f, 0.5f, 0.45f));
            const float wallHeight = 2f, wallThickness = 0.5f;
            float wallCentre = ArenaHalf + wallThickness * 0.5f, wallLength = ArenaHalf * 2f + wallThickness * 2f;
            AddWall(new Vector3(0f, wallHeight * 0.5f, wallCentre), new Vector3(wallLength, wallHeight, wallThickness), wallMaterial);
            AddWall(new Vector3(0f, wallHeight * 0.5f, -wallCentre), new Vector3(wallLength, wallHeight, wallThickness), wallMaterial);
            AddWall(new Vector3(wallCentre, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, wallLength), wallMaterial);
            AddWall(new Vector3(-wallCentre, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, wallLength), wallMaterial);

            var cam = Camera.main != null ? Camera.main : new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(player, false);
            cam.transform.localPosition = new Vector3(0f, 4f, -8f);
            cam.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);

            // HawkesNetAvatars joins in OnEnable: switched off while its fields go in, on again to join.
            gameObject.SetActive(false);
            avatars = gameObject.AddComponent<HawkesNetAvatars>();
            avatars.Project = Project;
            avatars.LiveHosts = LiveHosts;
            avatars.Area = Area;
            avatars.PlayerName = "Player " + Random.Range(100, 999);
            avatars.Local = player;
            avatars.RemotePrefab = remote;
            gameObject.SetActive(true);
        }

        // An edge wall: a visible box with no collider (the scene moves by transform, see Awake).
        static void AddWall(Vector3 centre, Vector3 size, Material material)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Edge wall";
            Destroy(wall.GetComponent<BoxCollider>());
            wall.transform.position = centre;
            wall.transform.localScale = size;
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }

        static string Others(int count) => count == 1 ? "1 other" : count + " others";

        void Update()
        {
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 0.01f)
            {
                player.position += move.normalized * Speed * Time.deltaTime;
                player.rotation = Quaternion.LookRotation(move);
            }
            // The capsule stands on the ground (centre at 1 m, radius 0.5 m) and stays inside the walls, whatever the input.
            float limit = ArenaHalf - 0.5f;
            var p = player.position;
            player.position = new Vector3(Mathf.Clamp(p.x, -limit, limit), 1f, Mathf.Clamp(p.z, -limit, limit));
        }

        void OnGUI()
        {
            var lobby = avatars != null ? avatars.Lobby : null;
            if (lobby == null) return;
            string room = lobby.RoomName.Length > 0 ? lobby.RoomName : "-";
            string problem = lobby.LastProblem.Length > 0 ? "\n" + lobby.LastProblem : "";
            GUI.Label(new Rect(10, 10, 640, 60), "Hawkes Net: " + lobby.Status + " · room " + room + " · " + Others(avatars.Remotes.Count) + problem);
        }
    }
}
