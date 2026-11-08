# Avatars sample

1. Make an empty scene, add an empty GameObject and put **HawkesNetAvatarsSample** on it.
2. Set **Project** to your account's project (`usr_...`, from https://net.hawkesgen.ai) and list your game's site there
   under "Your game's sites". (Or play it on localhost first: test rooms always let your own machine in, once the
   project has a site listed.)
3. Build for WebGL (Build And Run) and open the page in two tabs. Each opens on a start screen (`HawkesNetStartScreen`): type a
   name, press Join (or Enter); the status line says connecting, online or what failed, and Esc leaves. Then each tab walks a capsule with WASD and sees the other's, with its colour and a floating name.
4. Press **1**, **2** or **3** to wave, hop or spin: the other tab sees the emote, with a caption over the name.
   The colour and the emote travel in the pose's `Extra` values (see `Style` in the sample): copy that into your own game.
5. **On a phone** the same build shows a joystick (drag a thumb anywhere on the left half) and three emote buttons (right);
   tapping the name or room on the Join form raises the phone keyboard. Add `?touch=1` to the address to see them on a
   desktop browser. For the canvas to fill the screen, the page needs the demo's style sheet: `DevTools/demo-web/mobile.css`
   in the repo (copy it beside your `index.html` and link it, or use a WebGL template with a full-viewport canvas).
6. Add `?lag=150&jitter=30` to one tab's address: the other player's capsule still glides and turns smoothly, just
   further behind (the line over the controls says how far). That is `HawkesNet.SimulatedLatency` / `SimulatedJitter`.

In the Editor nothing joins (Hawkes Net runs in the page): the status reads `offline` with the problem `webgl-only`.

If the ground and capsules are magenta in the build, their shader was stripped: add **Standard** (or your render
pipeline's Lit shader) to Project Settings > Graphics > Always Included Shaders.
