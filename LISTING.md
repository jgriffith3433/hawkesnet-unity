# Asset Store listing: Hawkes Net

**Requires a free hawkesgen.ai account and a Hawkes Net project (net.hawkesgen.ai). Games connect to Hawkes Net's
servers; free projects have limits (rooms, players), paid accounts more.**

## Summary (short)

Multiplayer rooms for Unity, WebGL first: shared lobbies, avatars, messages and a shared clock on Hawkes Net's relay,
with no server of your own.

## Description

Hawkes Net puts your players in the same rooms with a few lines of C#.

- **Shared rooms by area** (`HawkesNetLobby`): one room per area for everyone, live and test rooms kept apart, the next
  shard when a room is full, rejoining by itself, one status for your HUD.
- **Avatars** (`HawkesNetAvatars`): everyone else drawn from your prefab, played back smoothly.
- **Messages** that arrive once and in order, or newest-wins for things sent often; **your own text** sent as is.
- **A shared clock** for timers and synced events.
- **Players in the background** show as away, and a host whose game is hidden hands the room to a present player.
- **WebGL** builds use Hawkes Net's web SDK in the page (WebSocket relay; peer to peer for small rooms where your project
  allows it). **The Editor and desktop builds** join the same rooms with a managed client: press Play and it joins your
  project's test room.
- **No server needed** for a game on its own website (list your sites on net.hawkesgen.ai); games with logins hand out
  tickets from their own server with one HTTPS call.
- A live dashboard at net.hawkesgen.ai: players online, rooms and who is in them, tickets and usage.
- Includes an Avatars sample. MIT-licensed source: github.com/jgriffith3433/hawkesnet-unity.

## Technical details

- Unity 2021.3 or later. WebGL, Windows, macOS, Linux (Editor and standalone).
- No dependencies.
- Requires an internet connection and a Hawkes Net project.
