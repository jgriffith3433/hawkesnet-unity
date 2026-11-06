# Changelog

## 0.8.1 (2026-11-06)

- **Fix: a page can skip the start screen.** `HawkesNetStartScreen` joins at once, with the name and room in the fields,
  when the page's address has `?autojoin=1` or a playtest run (`?playtest=<scenario>`, except `start-screen`). Enter now
  also joins from `Update` (legacy input), so a key press sent by a script reaches Join even when the name field takes
  the event in `OnGUI`. Hawkes Studio's smoke run was stuck on the form and never connected.

## 0.8.0 (2026-11-06)

- **Smooth remote avatars: `HawkesNetTrack`.** `HawkesNetAvatars` now plays every other player through a track (the web
  SDK's rules, so Unity and web pages draw a player alike): drawn between the two poses around a moment a little in the
  past, position lerped and facing turned the short way, so the avatar moves every frame; the delay is learned per player
  (how late their poses arrive, how often, how much that varies) and eased in, so a slow or jittery connection costs a
  little delay instead of stutter; a late pose is guessed along the velocity and turn (up to `MaxExtrapolation`, 250 ms)
  and the guess's error is blended out in about a tenth of a second; a player who stood still starts from where they
  stood; a correction of the room's clock never shows as a jump. `Remote.Track` (Delay, Ahead, Jitter...),
  `Remote.Position`, `Remote.Yaw` and `Remote.Velocity` say what is drawn. Games with their own avatars can use
  `HawkesNetTrack` with `HawkesNetLobby.PoseChanged` (`Push(pose, room.Now)`, `Sample(room.Now, out position, out yaw)`).
  At 150 ms of latency the old playback stood still in about half the frames and jumped; the track moves every frame
  without a jump (`DevTools/TrackTest`).
- **`HawkesNetAvatars.Delay` is now the least delay** (default 0: the learned one). A scene that saved 120 keeps at least 120.
- **`HawkesNet.SimulatedLatency` and `SimulatedJitter`** (ms): the others' poses arrive that much later, in order, to feel a
  slow connection. Both transports; on WebGL the page can set `window.__hawkesNet.lag` / `.jitter` too.
- **Avatars sample:** the player turns smoothly towards where it walks (`TurnSpeed`, 720 degrees a second), so the others
  see a turn rather than a snap; `?lag=150&jitter=30` on the page's address simulates a slow connection; the HUD says how
  far behind the others are drawn (and when one is being predicted).

## 0.7.0 (2026-11-06)

- **`HawkesNetStartScreen`: a start screen in one component.** Name and room fields, a Join button (or Enter), and a status
  line that says *Connecting...*, *Online · room x · N others* or *Failed: <reason> (<code>)* with the seconds to the next
  try and a Try again button. Once online it is a status bar with Leave (Esc), so the player can change name or room. The
  name is remembered (PlayerPrefs) and can come from the page's `?name=`. With no `project` or `ticketUrl` it shows the
  first-run steps instead of a failed join. `Blocking` tells your game to ignore the keyboard while the player types; the
  status line is also logged (`[HawkesNetStart] ...`, the browser console on WebGL).
- **`HawkesNetLobby.RetryIn` and `RetryNow()`**: the seconds until the lobby joins again after a refusal or a loss, and a
  way to join again at once.
- **Avatars sample** uses the start screen (Avatars' Join On Enable is off; the screen joins).

## 0.6.3 (2026-11-06)

- **Avatars sample: colours, floating names and three emotes.** Each player gets a colour (random; if two players land on
  nearly the same hue, the one whose id sorts last moves to the free hue farthest from the others), a name floating above
  the head in that colour, and three emotes on the keys 1, 2 and 3: wave (rocks side to side), hop, spin, each with a
  caption above the name. The colour and the emote ride in the pose's Extra values (`HawkesNetAvatars.Extra`, read as
  `Remote.Extra`), so your own game can do the same. Players are now an empty root with a capsule body and a nose, so a
  spin shows; the emote animates the body, not the root the room moves. No package API changed.

## 0.6.2 (2026-11-06)

- **Avatars sample: a bounded arena.** The ground is now a square of 2 x `ArenaHalf` metres (12 by default, was 50 by 50)
  with four edge walls, and the player's capsule stays on the ground inside them whatever the input: walking for minutes
  no longer takes it off the ground.
- **The HUD says "1 other"** (not "1 others") when one other player is in the room.

## 0.6.1 (2026-11-03)

- **Avatars sample: no more magenta in WebGL.** The sample's ground and capsules came from `CreatePrimitive`, whose
  Default-Material uses the Standard shader; nothing else in the scene references it, so a WebGL build stripped it and
  drew everything in the error colour. The sample now makes its materials from a shader found by name. Add that shader
  (Standard, or your render pipeline's Lit) to Project Settings > Graphics > Always Included Shaders in your build.

## 0.6.0 (2026-10-28)

- **Hawkes Net is at https://net.hawkesgen.ai** (hawkesgen.com is gone). The default endpoint, the SDK file and counts use it.
  Scenes saved with the old address (https://net.hawkesgen.com, which Unity stored as the default) count as unset, so
  they reach the new one without being opened again; a server of your own still wins.

## 0.5.1 (2026-10-27)

- **Staging sites play on their own Hawkes Net.** In a WebGL build the page's `window.HAWKES_NET_ENDPOINT` (an origin)
  is used before https://net.hawkesgen.ai for joins, the SDK file, background play and counts. Hawkes Studio sets it on
  beta sites, so a game's beta plays on beta Hawkes Net and its live site on the real one, with no change to the game.
  An `endpoint` or `sdkUrl` you set yourself still wins (the live address counts as unset: scenes save the default).

## 0.5.0 (2026-10-26)

- **The Editor and desktop builds join too.** Outside WebGL a managed client (`HawkesNetClient`: .NET WebSocket and
  HTTP, no dependencies) joins the same rooms on the relay with Hawkes Net's JSON protocol: messages, text, poses,
  peers, the host, the shared clock, away. In the Editor, with no `ticketUrl` or `key`, it joins your project's test
  rooms (`t-...`) with no key, so Play mode just works; a desktop build uses `ticketUrl` (your server) or, for servers
  and bots only, `key`.
- `HawkesNetOptions.key` and `configJson`. `HawkesNet.Count` works outside WebGL too.
- The package's home is its own repo, `jgriffith3433/hawkesnet-unity` (MIT).

## 0.4.0 (2026-10-26)

- **In the background** (a hidden tab, a minimised or covered window): the room stays at full speed (the web SDK's
  background clock), the player shows as away to the others (`Room.Away`, `OnPeerAway`, `HawkesNetLobby.PeerAway`;
  `IsAway`, `OnAway` for your own), and an away host hands the room to a present player (`HostReason` "host away").
  `Room.SetAway(bool)` for a game paused in its own menu.
- `HawkesNet.RunInBackground = true` keeps Unity running in the background (its main loop from Hawkes Net's background
  clock, about 60 times a second; it also sets `Application.runInBackground`). Off by default.

## 0.3.0 (2026-10-26)

- The relay by default (`HawkesNetOptions.transport` is `"relay"`): peer to peer (`"auto"`) only where the project
  turns it on (net.hawkesgen.ai > your project > Settings).
- Every project belongs to a hawkesgen.ai account: make it on net.hawkesgen.ai, pass its id as `project`. Download
  this package there (Add package from tarball).

## 0.2.1 (2026-10-26)

- A game with no server of its own joins with its account's project (`usr_...`) once its sites are listed on
  net.hawkesgen.ai ("Your game's sites"); localhost always may, in test rooms. Readme: "Your own game: getting in".
- Sample **Avatars**: one script makes a walking capsule and everyone else's, through `HawkesNetAvatars`.
- No code changes in Runtime.

## 0.2.0 (2026-10-26)

- `HawkesNetLobby`: shared rooms by area (live, test and playtest names, shards when full, rejoining, one status).
- `HawkesNetAvatars`: drop-in avatars from a prefab.
- The room: `SendText`, pose extras, `OnPose`/`OnStatus`, `Order`/`PeerOrder`, `ClosedBecause`, `HawkesNet.Count`.

## 0.1.0 (2026-10-25)

- `HawkesNet.Join(room, options)`: messages, poses, peers, host, transport, through the web SDK in the page.
