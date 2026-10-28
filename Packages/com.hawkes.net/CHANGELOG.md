# Changelog

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
