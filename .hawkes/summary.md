# drop-recover scenario: named for beta, events show each criterion

## What was wrong
`drop-recover` and `reconnect` were already registered in the demo page's playtest contract (template `index.html`) and implemented in `unity-playtest.mjs`. But `.hawkes/scenario.txt` still named `camera-feel`, so beta never played drop-recover. Its log showed `recoveryMs`/`identityMatch` and the safe-area result only as metrics, not as timeline events.

## What changed
- `.hawkes/scenario.txt`: `drop-recover` (joins through the start screen or the Join tap at phone size, 390x844, then a real 5 s network drop; no start point needed, about 90 s).
- `DevTools/demo-web/unity-playtest.mjs`: new `safe-area` event (`bannerInsideSafeArea true/false`), and the `recovered` event now states `recoveryMs` and `identityMatch`. No scenario logic changed.

## Checks
- `node --check` on the script. Unity compile not run here (no C# touched).
- Not verified: a real beta run (not allowed to start one).
- Conflicts: none. Sparse checkout hid DevTools; I disabled it locally.
- GPU: none needed.
