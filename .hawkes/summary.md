# Damped follow camera for the Avatars sample (0.15.0)

## What was wrong
The sample's camera was parented to the avatar at a fixed local offset, so it clipped through edge walls and props and could snap with the body. There was no turn-rate cap, no idle recentre, and the play-mode scene audit from k_3010adf3e6f0 only counted insides on a dedicated orbit probe — not during a wall-walking run with the gameplay cam.

## What changed
- **`HawkesNetFollowCamera`** (Runtime): unparented damped follow — SphereCast pull-in (fast) / ease-out (slow), yaw/pitch damping capped at 30°/frame, idle recentre behind the avatar after 2 s still, right-mouse / right-half one-finger orbit. Publishes `window.__hawkesNet.camera` via `HawkesNet.ShowedCamera`.
- **Scene audit hook:** live `followInsideFrames` / `followInside` on `window.__hawkesNet.scene` (and mirrored from the camera bridge in the jslib).
- **Sample + demo twins** both `Attach` the follow cam; WebGL template adds `?touch=1` for `camera-feel` (and `drop-recover`) like `phone`.
- **Playtest** scenario `camera-feel` (named in `.hawkes/scenario.txt`): wall walk → turn cap → idle recentre → touch orbit.
- Package **0.15.0**; EditMode tests for pull-in, turn cap, idle recentre.

## Checks
- `package.json` parses; `npm pack` → `com.hawkes.net-0.15.0.tgz` (not committed).
- `node --check` on `unity-playtest.mjs`.
- Unity compile not run here (no editor on this machine); Studio's pre-PR C# scan + CloudBuild will compile.
- Conflicts: none.

## Reviews
- **reviewer:** merge — criteria covered; non-blocking notes addressed (summary/try rewritten; dist not committed; template `touch=1` for `camera-feel`).
- **fun-reviewer:** **demo** (expected for this package practice green). Gaps (more dressing, a second interaction verb, Drop as sound/voice) are out of scope for "keep the change inside the sample's follow camera."

## Left for later
Arena living-world gaps the fun reviewer listed (ambient bed, sit/kick verbs, Drop as heard beat) — separate roadmap items, not this camera pass.
