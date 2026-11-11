# Playtest scenario `page` now shows each page criterion

**What was wrong.** Beta's playtest ran the default scenario (`smoke`), so the WebGL page criteria (early hook, deeper-path relocation, error card) were never played or logged. The existing `page` scenario also never checked the deeper-path relocation, and never reached the playable demo.

**What I changed** (feature untouched; test side only)
- `DevTools/demo-web/unity-playtest.mjs`, scenario `page`:
  - `hook-early` event: the inline stub's time (limit 1000 ms), logged first.
  - new `deep-path` check/event: takes an address two folders below the page with a query string, resolves the template's own probe list (`./`, `../`, `../../demo-root.json`, must be relative) against it, asks each once, and checks the first that answers lands on this demo's index with the query string unchanged.
  - `error-card` event now carries the reason shown; the card is cleared afterwards.
  - new `demo-playing` check: Join with Enter, then W and D key presses move the avatar (real input, no teleport).
- `.hawkes/scenario.txt`: `page` (about 30 s, no start point needed).

**How I checked.** `node --check` on the script. Not played here: no browser or Unity build on this machine (Studio plays it on beta).
**Caveat.** The deeper-path check simulates the relocation in the page (a static host has no real deeper page to open); it uses the template's own `tries` list, so it fails if the list changes.
No conflicts.
