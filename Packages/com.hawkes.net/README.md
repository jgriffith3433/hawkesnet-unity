# Hawkes Net for Unity

Multiplayer for Unity games on Hawkes Net (https://net.hawkesgen.ai). WebGL builds run Hawkes Net's web SDK in the page
(the relay, or peer to peer while a room is small where your project allows it); **the Editor and desktop builds join the
same rooms on the relay with a managed client**, so you can try it in Play mode. Three layers; most games use the first
or second.

## Quickstart

1. **Install.** Package Manager > + > Add package from git URL:
   `https://github.com/jgriffith3433/hawkesnet-unity.git?path=/Packages/com.hawkes.net#v0.9.1`
2. **Make a project.** Sign in at [net.hawkesgen.ai](https://net.hawkesgen.ai) (free) and create a project; copy its id (`usr_...`).
3. **Check your setup.** Window > Hawkes Net > Setup. Paste the id: the window flags a missing or odd id, a scene with no
   player, and anything else a first join needs.
4. **Add the sample.** Package Manager > Hawkes Net > Samples > Import *Avatars*, put its script on an empty GameObject and
   set `Project` to your id (the Setup window's "Use it on the scene's Avatars" does it).
5. **Press Play.** You join your project's test room with no key. Build for WebGL and open two tabs, or run a second
   Editor, to meet yourself.

**In the Editor:** with no `ticketUrl` or `key`, a join goes to your project's test rooms (`t-...`) with no key: press
Play. **A desktop build:** set `ticketUrl` to your server's ticket route (it asks hawkesgen.ai with your API key). `key`
is for servers, bots and tests only: never ship a key in a game.

## Your own game: getting in

1. Sign in on **https://net.hawkesgen.ai** with a hawkesgen.ai account (free) and make a **project**: its id is what
   your game passes as `project`. Its page shows players online, rooms and who is in them, tickets and usage, live.
2. **No server** (most WebGL games: a static page, itch.io): list where the game is served under the project's Settings
   (`https://mygame.com`, `https://mygame.itch.io`; a paid account). Pages there join with no key, and your own machine
   (`http://localhost`) always may, in test rooms (`t-...`): Build And Run and play, on any account.
   **A server** (your logins, matchmaking): set `ticketUrl` to your route instead (section 3).
3. Put your sites' host names in the lobby's `liveHosts` (below): there your players share the live rooms; anywhere else
   (a beta copy, localhost) the rooms are test rooms, free and apart from them.
4. Try it: Package Manager > Hawkes Net > Samples > **Avatars** (one script on an empty GameObject: a capsule you walk
   with WASD and everyone else's). Build for WebGL and open two tabs.

5. **A start screen, ready made:** put `HawkesNetStartScreen` next to your `HawkesNetAvatars` (turn its *Join On Enable* off, set
   the start screen's *Avatars*), or call `startScreen.Attach(lobby, "lobby")` for your own `HawkesNetLobby`. Players get a
   name and a room field, a Join button (or Enter), and one status line: *Connecting...*, *Online · room x · 2 others*, or
   *Failed: the room refused the join (ticket: ...). Trying again in 8 s.* with a Try again button; once online it is a
   status bar with Leave (Esc). The name is remembered and can come from the link (`?name=Ada`). Read `Blocking` to ignore
   the keyboard while the player types. With no `project` or `ticketUrl` set it shows the first-run steps instead of failing.

Every room runs on the relay. Peer to peer is a project setting (off by default) for a game that asks for `transport = "auto"`.

## 1. Shared rooms: `HawkesNetLobby` (the standard way)

One shared room per area ("lobby", "level-1"), everyone in it, kept joined. It names the room for where the page runs,
spills into the next shard when a room is full, joins again by itself when the connection is lost or refused, and gives
one status for the HUD. Subscribe once: its events carry on across rejoins and shards.

```csharp
using Hawkes;

var lobby = new HawkesNetLobby(new HawkesNetOptions { project = "mygame", name = "Ada" }, "mygame.com", "www.mygame.com");
lobby.Joined += room => Debug.Log($"in {room.Name} as {room.Id} over {room.Transport}");
lobby.PeerJoined += (id, name) => Spawn(id, name);
lobby.PeerLeft += (id, why) => Despawn(id);
lobby.Message += (kind, data, from) => { if (kind == "chat") Show(from, data); };
lobby.PoseChanged += (id, pose) => Move(id, pose.position, pose.yaw, pose.extra);
lobby.StatusChanged += status => chip.text = status;     // off, connecting, online, reconnecting, offline, full
lobby.Join("level-1");

void Update() {
    lobby.Room?.Pose(player.position, player.eulerAngles.y, "{\"hat\":\"crown\"}");   // every frame is fine
    if (pressedWave) lobby.Room?.SendText("emote", "wave");
}
```

Room names (the project's test prefix `t-` keeps beta and playtests apart from live: free, out of the counts):

| Where the page runs | Room |
|---|---|
| one of the live hosts you pass | `level-1`, then `level-1.2`, `level-1.3`... when full |
| anywhere else (beta sites, localhost) | `t-level-1` |
| a playtest (`?room=<id>`: Gen's `playtest.run` with `peers`) | `t-<id>/level-1` |

`Prefix` goes before the area (a protocol version, `"p1/"`, so pages that can't talk never meet). `NameFor(area)` gives the
full name, `HawkesNet.Count(project, rooms, done)` how many are in rooms you aren't in (Gen's public counts, for a
project with its own sites).

## 2. Drop-in avatars: `HawkesNetAvatars`

A component: set `Project`, `Area`, `LiveHosts`, `Local` (your player) and `RemotePrefab`. It joins through a lobby,
sends your pose, and keeps one prefab per other player, moved every frame by a `HawkesNetTrack`. `Extra` rides with your
pose; read `Remotes[id].Extra` on the others.

### Smooth remote players: `HawkesNetTrack`

Poses arrive about 30 times a second, late and unevenly. A track draws each player a little in the past, between the two
poses around that moment (position and facing), so they move every frame and turn the short way. How far in the past is
learned per player: how late their poses arrive, how often, and how much that varies, eased in so playback never jumps.
When a pose is late the player is carried on along its velocity and turn (up to `MaxExtrapolation`, never more than 250 ms; after that it holds its last pose), and the
guess's error is blended out in about a tenth of a second when the real pose comes. `HawkesNetAvatars.Delay` is the
least delay (0: the learned one); `Remotes[id].Track.Delay` and `.Ahead` say how far behind and how far predicted.

With your own avatars, one track per player:

```csharp
var tracks = new Dictionary<string, HawkesNetTrack>();
lobby.PoseChanged += (id, pose) => { if (!tracks.TryGetValue(id, out var t)) tracks[id] = t = new HawkesNetTrack(); t.Push(pose, lobby.Room.Now); };
void Update() {
    foreach (var kv in tracks)
        if (kv.Value.Sample(lobby.Room.Now, out var position, out var yaw)) avatars[kv.Key].SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
}
```

To feel a slow connection: `HawkesNet.SimulatedLatency = 150; HawkesNet.SimulatedJitter = 30;` (ms; the others' poses
arrive that much later, in order). The Avatars sample reads `?lag=150&jitter=30` from the page's address.

## In the background

Players hide tabs, minimise and cover windows all the time, and the browser stops a Unity game there. Hawkes Net keeps
the room going (its web SDK's sends run on a clock browsers don't slow), shows the player as away to the others, and a
host whose page is away hands the room to a present player, so whatever the host simulates keeps moving:

```csharp
lobby.PeerAway += (id, away) => avatars[id].SetDimmed(away);   // or room.OnPeerAway / room.Away
room.OnPeerBack += id => avatars[id].SetDimmed(false);         // same moment as OnPeerAway(id, false)
room.OnHost += id => Debug.Log($"host {id} ({room.HostReason})"); // "host away" when handed over
room.OnReconnecting += () => hud.Show("reconnecting…");
room.OnReconnected += () => hud.Show("back");
```

A game that must keep running while hidden (a turn timer, a fight, a host's simulation others wait on):

```csharp
HawkesNet.RunInBackground = true; // also sets Application.runInBackground
```

Unity's main loop then runs about 60 times a second from Hawkes Net's background clock until the page is seen again.
Presence games usually leave it off: the player is shown as away and the host role moves on.

## 3. The room: `HawkesNet.Join`

```csharp
var room = HawkesNet.Join("lobby", new HawkesNetOptions {
    ticketUrl = "https://mygame.example/ticket",   // your server: POST /v1/net/rooms/ticket on hawkesgen.ai with your API key
    name = "Ada",                                  // (or project = "mygame" for a page on that project's own sites)
});
room.OnReady += r => Debug.Log($"in {r.Name} as {r.Id} over {r.Transport}");
room.On<Chat>("chat", (msg, from) => Show(room.Peers[from], msg.text));
room.Send("chat", new Chat { text = "hello" });            // JSON (a JsonUtility object or a JSON string)
room.SendText("say", "plain text arrives as it was sent");
room.OnPose += (id, pose) => MoveAvatar(id, pose.position, pose.yaw);   // or read room.Poses
```

- **Poses** are the avatar channel: the relay sends near players every bundle, far ones 5 a second and the farthest once
  a second, so a big room stays cheap. `extra` is a small JSON object that rides with the pose; a key you stop sending is
  dropped for the others too. `HawkesNetPose.at` is when it was, on the room's clock (`room.Now`).
- **Messages**: reliable ones arrive once and in order (also across reconnects and the move to the relay); an unreliable
  one is replaced by a newer one of its kind still queued. `room.Order` and `PeerOrder` are the join order (the oldest is
  the host: `room.Host`).
- **Your ticket route** receives `{ room, name }` from the page and answers with what hawkesgen.ai gave it:
  `POST https://hawkesgen.ai/v1/net/rooms/ticket` with `Authorization: Bearer hg_...` and `{ "room", "name", "transport", "config" }`.
  Rooms are in your own project; usage is on your account. A Hawkes game uses its project's public door instead (`project`).
- WebGL builds only: the web SDK runs in the page (loaded from `https://net.hawkesgen.ai/sdk/hawkes-net@1.mjs`). In the
  Editor `Join` reports the problem `webgl-only` (a lobby's status is then `offline`): keep an Editor stand-in of your own.
- Events come on the main thread. Changed poses are read once a frame.
- Install: download it from net.hawkesgen.ai (`/sdk/com.hawkes.net.tgz`) and Package Manager > Add package from tarball, or
  a copy in your game's `Packages/` (a build machine that can't see this folder). `CHANGELOG.md` says what changed.
