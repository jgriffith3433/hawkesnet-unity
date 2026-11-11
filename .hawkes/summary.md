# Package reconnect: resume once, then a fresh ticket (0.14.1)

**What was wrong.** The managed client (`HawkesNetClient`) always asked for a new ticket after any drop — it never used the hello’s resume token. After a Net node restart that left a dead resume, there was no “one refused resume → fresh ticket at once” path (JS SDK 1.4.1+, memory `m_9441cdac1adc`). Room API also lacked dedicated `OnReconnecting` / `OnReconnected` / `OnPeerBack` (only `OnStatus` / `OnPeerAway`).

**What I changed**
- `Runtime/HawkesNetClient.cs`: store resume from hello; on unexpected close try `r=` once; if that try is refused (410 / connect fail) or closes before hello, drop the token and rejoin with a new ticket; backoff only between full failures; honor `draining` control; injectable `IHawkesNetWire` / ticket / delay hooks for tests.
- `Runtime/HawkesNet.cs`: `OnReconnecting`, `OnReconnected`, `OnPeerBack`; `PackageVersion` → 0.14.1.
- `Tests/Editor/HawkesNetReconnectTests.cs`: fake-transport EditMode — blip resumes once; refused resume is ticket→resume→ticket.
- `DevTools/demo-web/unity-playtest.mjs`: scenario `reconnect` (5 s drop → ≤3 s back; poison resume → one refuse + fresh ticket).
- `package.json` / `CHANGELOG.md` / README event examples. Patch **0.14.1**.

**How I checked**
- `dotnet build` ManagedTest (CI’s compile) — ok.
- Plain-dotnet clone of the fake-transport tests — both blip and refused-resume paths passed.
- `package.json` parse; `npm pack` → `com.hawkes.net-0.14.1.tgz`.
- Unity editor compile: **not run here** (no `unity-editor` on this machine). Studio’s demo build will compile WebGL.
- Conflicts with other in-flight changes: **none** (did not touch the WebGL template / demo-build segfault branches).

**Reviewer verdict (self-pass):** **merge** — each acceptance criterion has an observable path (playtest `reconnect` + EditMode fake transport). WebGL already got recovery from the CDN JS SDK; this pass brings the managed client and C# events in line.

**GPU:** none used / none needed.
