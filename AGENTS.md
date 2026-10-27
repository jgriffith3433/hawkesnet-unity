# hawkesnet-unity — agent guide

Hawkes Net's Unity package (`Packages/com.hawkes.net`, UPM name `com.hawkes.net`), public, MIT. This repo is its home
since 2026-10-26 (J: make hawkesnet-unity like hawkesgen-unity); before that it was in hawkesgen, then hawkesnet's
`sdk/unity`. `README.md` has the layout, `PUBLISHING.md` the Asset Store steps.

- **Two transports, one API.** WebGL: `Plugins/WebGL/HawkesNet.jslib` runs the web SDK (`@hawkesgen/net`, loaded from
  `https://net.hawkesgen.com/sdk/hawkes-net@1.mjs`). Everywhere else: `HawkesNetClient.cs` (plain .NET, the JSON protocol
  `hawkes-net.v2` on the relay). Both produce the same events (`HawkesNet.Event`) and pose lists, so `HawkesNetRoom`,
  the lobby and the avatars don't know which runs. A feature added to one must be added to the other.
- `HawkesNetClient.cs` uses no UnityEngine: test it with `dotnet run --project DevTools/ManagedTest -- <endpoint>`
  (a Hawkes Net service: `npm run dev` in a hawkesnet checkout serves one on :8786).
- Never write U+2028/U+2029 as escapes in a char literal through a tool that unescapes them: use `(char)0x2028`.
- Keep `.meta` files with their files (GUIDs are stable: games reference them).
- **Consumers:** Yooba Land and Sick Of It install it by git URL (`Packages/manifest.json`); bump the tag there after a
  release. Tag every release `v<version>` (the git URL pins it).
- Never put a key in a sample or a test.
