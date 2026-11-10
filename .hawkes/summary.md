# Beta build failure: skipProviders

**What was wrong:** `Bad params for playtest.run: no param "skipProviders"` is Studio sending Gen a provider hint that `playtest.run` doesn't take. It is not a Unity compile error. No file in this repo (C#, jslib, workflows, DevTools) uses `skipProviders`.
Studio's `src/playtest-finish.ts` already strips a stale `skipProviders` on the second try (see `src/gen-params.ts`), so the fix is Studio-side.

**Changed:** nothing in code. The scene audit from #30 (HawkesNetSceneAudit, EditMode tests, smoke bridge check) is kept as is.

**Checked:** package.json parses (0.14.0). Unity compile not run here. No Unity editor, no `npm pack` needed (no package changes).

**For J:** re-run beta's playtest with the `smoke` scenario. If it still fails with the same message, the running Studio needs the `playtest-finish` change deployed.
No conflicts. No GPU work, so no estimate.
