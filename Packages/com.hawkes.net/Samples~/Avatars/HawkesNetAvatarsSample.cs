using UnityEngine;

namespace Hawkes.Samples
{
    /// <summary>
    /// The smallest multiplayer scene: a capsule you walk with WASD (or the arrow keys), a camera behind it, and a
    /// capsule for every other player in the area's shared room, through HawkesNetAvatars. Put it on an empty
    /// GameObject in an empty scene, set Project, build for WebGL and open two tabs.
    /// </summary>
    public sealed class HawkesNetAvatarsSample : MonoBehaviour
    {
        [Tooltip("Your account's project (usr_...), from https://net.hawkesgen.com.")]
        public string Project = "";
        [Tooltip("Your game's own host names (mygame.com): there the rooms are live, anywhere else test rooms.")]
        public string[] LiveHosts = new string[0];
        public string Area = "sample";
        public float Speed = 5f;

        Transform player;
        HawkesNetAvatars avatars;

        void Awake()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            player = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
            player.position = new Vector3(Random.Range(-4f, 4f), 1f, Random.Range(-4f, 4f));
            player.GetComponent<Renderer>().material.color = Color.HSVToRGB(Random.value, 0.6f, 0.9f);

            // Everyone else: a grey capsule, kept inactive as the template.
            var remote = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            remote.name = "Remote template";
            remote.SetActive(false);

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

        void Update()
        {
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 0.01f)
            {
                player.position += move.normalized * Speed * Time.deltaTime;
                player.rotation = Quaternion.LookRotation(move);
            }
        }

        void OnGUI()
        {
            var lobby = avatars != null ? avatars.Lobby : null;
            if (lobby == null) return;
            string room = lobby.RoomName.Length > 0 ? lobby.RoomName : "-";
            string problem = lobby.LastProblem.Length > 0 ? "\n" + lobby.LastProblem : "";
            GUI.Label(new Rect(10, 10, 640, 60), "Hawkes Net: " + lobby.Status + " · room " + room + " · " + avatars.Remotes.Count + " others" + problem);
        }
    }
}
