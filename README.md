# Hawkes Net for Unity

Multiplayer for Unity games on **[Hawkes Net](https://net.hawkesgen.com)**: shared rooms by area, avatars, messages, a
shared clock, players marked away when their game is in the background, and a host that hands the room on. WebGL builds
run Hawkes Net's web SDK in the page; the Editor and desktop builds join the same rooms with a managed client.

**Install** (Package Manager > + > Add package from git URL):

```
https://github.com/jgriffith3433/hawkesnet-unity.git?path=/Packages/com.hawkes.net#v0.5.0
```

or download `com.hawkes.net.tgz` from [net.hawkesgen.com](https://net.hawkesgen.com) (Add package from tarball), or get
it from the Unity Asset Store. Then make a free account and a project at [net.hawkesgen.com](https://net.hawkesgen.com)
and read [the package's README](Packages/com.hawkes.net/README.md).

```csharp
using Hawkes;

var lobby = new HawkesNetLobby(new HawkesNetOptions { project = "mygame", name = "Ada" }, "mygame.com");
lobby.Joined += room => Debug.Log($"in {room.Name} with {room.Peers.Count} others");
lobby.Message += (kind, data, from) => Debug.Log($"{from}: {kind} {data}");
lobby.Join("lobby");
```

## This repo

| Path | What |
|---|---|
| `Packages/com.hawkes.net/` | **The package** (what ships): `Runtime/HawkesNet.cs` (the room), `HawkesNetLobby.cs`, `HawkesNetAvatars.cs`, `HawkesNetClient.cs` (the managed client: Editor and desktop), `Plugins/WebGL/HawkesNet.jslib` (the web SDK's bridge), `Samples~/Avatars` |
| `DevTools/ManagedTest/` | The managed client end to end with plain `dotnet`, against a Hawkes Net service: `dotnet run --project DevTools/ManagedTest -- http://127.0.0.1:8786` |
| `DevTools/make_unitypackage.py` | The Asset Store `.unitypackage` (the package as `Assets/HawkesNet`), built without Unity |
| `PUBLISHING.md`, `LISTING.md` | Uploading to the Asset Store, and the listing's text |

The Unity project here (6000.3.7f1) is what the package is tested and uploaded from; the package supports 2021.3+.
The WebGL bridge's tests run against the web SDK in the [hawkesnet](https://net.hawkesgen.com) service's repo.

License: [MIT](LICENSE).
