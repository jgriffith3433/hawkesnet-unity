# hawkesnet-unity — agent guide

Hawkes Net's Unity package (`Packages/com.hawkes.net`, UPM name `com.hawkes.net`), public, MIT. This repo is its home
since 2026-10-26 (J: make hawkesnet-unity like hawkesgen-unity); before that it was in hawkesgen, then hawkesnet's
`sdk/unity`. `README.md` has the layout, `PUBLISHING.md` the Asset Store steps.

- **Two transports, one API.** WebGL: `Plugins/WebGL/HawkesNet.jslib` runs the web SDK (`@hawkesgen/net`, loaded from
  `https://net.hawkesgen.ai/sdk/hawkes-net@1.mjs`). Everywhere else: `HawkesNetClient.cs` (plain .NET, the JSON protocol
  `hawkes-net.v2` on the relay). Both produce the same events (`HawkesNet.Event`) and pose lists, so `HawkesNetRoom`,
  the lobby and the avatars don't know which runs. A feature added to one must be added to the other.
- `HawkesNetClient.cs` uses no UnityEngine: test it with `dotnet run --project DevTools/ManagedTest -- <endpoint>`
  (a Hawkes Net service: `npm run dev` in a hawkesnet checkout serves one on :8786).
- Never write U+2028/U+2029 as escapes in a char literal through a tool that unescapes them: use `(char)0x2028`.
- Keep `.meta` files with their files (GUIDs are stable: games reference them).
- **Consumers:** Yooba Land and Sick Of It install it by git URL (`Packages/manifest.json`); bump the tag there after a
  release. Tag every release `v<version>` (the git URL pins it).
- Never put a key in a sample or a test.

## How Hawkes Studio builds and releases it (2026-11-01)

Studio project `hawkes-net-unity` (engine unity, Jaycee's Studio cell `j-studio-4f12`). Work on `claude/*` branches,
pull requests into `beta` (they merge themselves once CI passes); `main` moves only when Jaycee takes beta live.
- **demo** (Hawkes Gen's cloud, Unity 6000.3.7f1): `-executeMethod Hawkes.Demo.Editor.HawkesNetDemoBuild.CloudBuild`
  (`Assets/HawkesNetDemo/`) makes a scene with the Avatars sample and builds it to WebGL, so every beta push checks that
  the package compiles for WebGL and gives a playable demo on the project's Studio beta site. Keep
  `Assets/HawkesNetDemo/HawkesNetDemo.cs` in step with `Samples~/Avatars`. The demo scene itself is made, not committed.
- **package** (Studio): `npm pack` of `Packages/com.hawkes.net`, the tarball games can install.
- **CI** (`.github/workflows/ci.yml`): package.json, every .meta, and the managed client compiles (plain .NET). The
  managed client runs end to end in hawkesnet's CI against the service.
- **Release** (`.github/workflows/release.yml`): when main gets a `package.json` version with no tag, it tags
  `v<version>` and makes a GitHub release with the tarball. Bump `package.json` and `CHANGELOG.md` together on beta.
  After a release, copy the package into hawkesnet's `sdk/unity/` (its download) and bump the games' manifests.

## Studio's playtest of the demo

The demo WebGL page is `Assets/WebGLTemplates/HawkesNetDemo` (`PlayerSettings.WebGL.template = PROJECT:HawkesNetDemo` in
`HawkesNetDemoBuild`): an inline `window.hawkesPlaytest` stub (status `loading` + loader progress) before the Unity
loader, relative `Build/` paths, `demo-root.json` next to `index.html` (so a deeper beta URL can `location.replace` back
to the demo and keep the query string), and a plain error card when the build cannot load. `unity-playtest.mjs` upgrades
the stub when the sample is ready. Scenario `page` checks the hook, the marker and the error card.

The demo build also carries Hawkes Studio's playtest bot: `HawkesNetDemoBuild` copies `DevTools/demo-web/` (outside Assets, so
Unity ignores it) beside the WebGL `index.html` and adds `unity-playtest.mjs` to it. The bot (`window.hawkesPlaytest`)
finds the Unity player's room in the page (`window.__hawkesNet`, HawkesNet.jslib), opens a second player there, walks the
capsule with real key presses and checks poses and messages both ways through the bridge (`smoke`), or meets real
browsers on one `?room=` (`two-players`). `net-playtest.mjs` is the same file as hawkesnet-js and hawkesnet-babylon's
`example/net-playtest.mjs`: change all of them.
