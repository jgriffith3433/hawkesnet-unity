# Avatars sample

1. Make an empty scene, add an empty GameObject and put **HawkesNetAvatarsSample** on it.
2. Set **Project** to your account's project (`usr_...`, from https://net.hawkesgen.ai) and list your game's site there
   under "Your game's sites". (Or play it on localhost first: test rooms always let your own machine in, once the
   project has a site listed.)
3. **Bake the humanoid** (once per project): menu **Hawkes Net > Bake Avatars humanoid**. That finds
   `RobotExpressive.glb` (this sample's `Models/`, or the demo project's `Assets/HawkesNetDemo/Models/`) through
   [glTFast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.20/manual/index.html)
   (`com.unity.cloud.gltfast`, already a package dependency) and writes `Resources/HawkesNetAvatar` with an Animator
   controller (1D idle/walk/run blend from each clip's root speed, plus wave / hop / spin). The demo WebGL cloud build
   runs the same bake before it compiles. Without the bake you still get a tinted capsule stand-in so the room joins.
4. Build for WebGL (Build And Run) and open the page in two tabs. Each opens on a start screen (`HawkesNetStartScreen`): type a
   name, press Join (or Enter); the status line says connecting, online or what failed, and Esc leaves. Then each tab walks a
   tinted humanoid with WASD and sees the other's, with its colour and a world-space floating name above the head
   (camera-facing, soft plate, hidden behind walls).
5. Press **1**, **2** or **3** to wave, hop or spin, and **Space** to jump: the other tab sees the emote or the jump
   (Jump → Land → Locomotion). Colour, emote (`e`/`n`) and jump count (`j`) travel in the pose's `Extra` values (see
   `Style` in the sample). Remotes blend walk/run from `HawkesNetTrack`'s smoothed velocity.
6. **On a phone** the same build shows a joystick (drag a thumb anywhere on the left half) and three emote buttons (right);
   tapping the name or room on the Join form raises the phone keyboard. Add `?touch=1` to the address to see them on a
   desktop browser. For the canvas to fill the screen, the page needs the demo's style sheet: `DevTools/demo-web/mobile.css`
   in the repo (copy it beside your `index.html` and link it, or use a WebGL template with a full-viewport canvas).
7. Add `?lag=150&jitter=30` to one tab's address: the other player's avatar still glides and turns smoothly, just
   further behind (the line over the controls says how far). That is `HawkesNet.SimulatedLatency` / `SimulatedJitter`.

**Model credit.** RobotExpressive by Tomás Laulhé /
[three.js](https://github.com/mrdoob/three.js/tree/dev/examples/models/gltf/RobotExpressive) (CC0). Idle, walk, run,
jump, wave and dance clips; tinted per player with a `MaterialPropertyBlock`. Soft push-apart (`MinGap` /
`SeparateSpeed`) still uses each avatar's `CapsuleCollider`.

In the Editor nothing joins (Hawkes Net runs in the page): the status reads `offline` with the problem `webgl-only`.

If the ground is magenta in the build, its shader was stripped: add **Standard** (or your render
pipeline's Lit shader) to Project Settings > Graphics > Always Included Shaders. The baked humanoid keeps its own
materials through the prefab reference.
