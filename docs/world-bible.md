# Hawkes Net for Unity: world bible

## Areas
**The Arena** — a small square practice green (about 24 m across) edged with low walls. Mood: clear daylight demo
space, nothing hidden. Players drop in from the Join screen, walk, jump the ramp and the step, wave at whoever else
is in the room. Ambient: open sky, soft footfalls implied by the locomotion blend; no music bed (the package is the
product). Exit: Leave / Esc back to the Join form.

## Landmarks
- **The Ramp** (southeast wall): a shallow slope, the place you first notice Jump → Land on uneven ground.
- **The Step** (northwest wall): a low platform for the capsule ground probe and a clean landing silhouette on video.
- **Corner posts and side benches**: frame the square; every prop's base sits on the ground (±2 cm).
- **Banner N**: a freestanding north placard on the Join-facing wall line (base on the ground, not hung in air).

## Who lives here
Tinted **RobotExpressive** humanoids (Tomás Laulhé / three.js, CC0), one per player. Each keeps a colour and a name
on a world-space nameplate above the head (soft dark plate, camera-copy billboard, hidden behind walls). Routines:
idle when still; walk/run from ground speed; Space jumps; keys 1–3 wave / hop / spin. Remotes mirror that from
HawkesNetTrack's smoothed velocity and the pose Extra (`e`/`n` emotes, `j` jumps), not from raw packets.

## What happens
Interactions: walk together, soft push-apart when too close, jump the ramp/step, trade emotes and jumps so every
screen plays the same clip. Events: another player joining (name + colour appear), latency HUD when remotes are
drawn behind; a player whose connection drops stays in the room for its grace (15 s) and is shown as a faded ghost with
"(away)" on the nameplate, then normal when back. Their own screen says "Reconnecting…" in a small banner inside the safe
area, then "Back in the room", with the same name and colour. Ambient: the arena itself (ground, walls, ramp, step). Over time: colours resolve if two players clash.
Smoke playtest runs a scene audit (ground contact, z-fight, inside-out, camera orbit) through the jslib bridge.
The `nameplates` playtest orbits a probe camera 360° around a remote and checks names never read mirrored, stay
readable on a phone, and vanish when occluded. The `drop-recover` playtest cuts a player's network for 5 s and checks the
banner, a recovery under 2 s with the same identity, and the ghost on the other side.

## Story beats
1. Join — type a name, enter the arena (gameplay, not a text dump).
2. Meet — another humanoid appears in their colour; you see them walk from the smoothed track.
3. Play — jump and emote; they play on the other screen; landings return to locomotion without sticking in the air.
3b. Drop — the network goes: "Reconnecting…", then "Back in the room" in the same body and colour; the others saw a ghost, not an empty spot.
