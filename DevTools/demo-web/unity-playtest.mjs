// Hawkes Studio's playtest bot on the WebGL demo's page (HawkesNetDemoBuild copies DevTools/demo-web beside index.html and
// adds this script). The Unity player's room lives in the page (HawkesNet.jslib keeps it in window.__hawkesNet), so the
// bot (net-playtest.mjs, the same file as hawkesnet-js and hawkesnet-babylon) opens a second player in that room, walks
// the Unity capsule with real key presses, and checks that the poses and messages go both ways through the bridge.
import { installNetPlaytest } from './net-playtest.mjs';

const LIVE = 'https://net.hawkesgen.ai';
// What the Unity side says about its start screen: HawkesNetStartScreen logs "[HawkesNetStart] <status> | <line>" to the
// browser console each time the status line changes (and "start screen up" when it shows). Kept from page load on.
const startLog = [];
for (const m of ['log', 'info', 'warn', 'error']) {
  const orig = console[m].bind(console);
  console[m] = (...a) => {
    try { const t = a.map(String).join(' '); const i = t.indexOf('[HawkesNetStart] '); if (i >= 0) startLog.push({ at: performance.now(), text: t.slice(i + 17).split('\n')[0].trim() }); } catch { /* never in the way */ }
    return orig(...a);
  };
}
const startUp = () => startLog.some((l) => l.text.startsWith('start screen up'));
const endpoint = () => (/^https?:\/\/[a-z0-9.-]+(:\d+)?$/i.test(String(globalThis.HAWKES_NET_ENDPOINT || '')) ? globalThis.HAWKES_NET_ENDPOINT : LIVE);
// The Unity player's room: the newest join the bridge holds that has a room.
const slot = () => { const rooms = globalThis.__hawkesNet?.rooms ?? {}; return Object.values(rooms).filter((s) => s && !s.gone && s.net).pop() ?? null; };
let sent = null;
let sentExtra = null; // the extra values (colour, emote) the Unity player sent with its newest pose
// The second player's look, sent with every pose it makes: an orange capsule, no emote yet (the sample's Style).
const botStyle = { c: '#ff9d2e', e: 0, n: 0 };
const main = () => {
  const s = slot();
  const net = s?.net ?? null;
  // What the Unity player sends (HawkesNet_Pose calls net.pose): its own place, as the other player should see it.
  if (net && !net.__hnptPose) {
    const pose = net.pose.bind(net);
    net.pose = (p, y, e) => { sent = Array.from(p).slice(0, 3); sentExtra = e && typeof e === 'object' ? { ...e } : null; return pose(p, y, e); };
    net.__hnptPose = true;
  }
  return net;
};
// Unity reads the keyboard from the page: a press on the canvas and the window, with the key's code as a browser sends it.
const CODES = { d: ['KeyD', 68], w: ['KeyW', 87], 1: ['Digit1', 49], 2: ['Digit2', 50], 3: ['Digit3', 51],
  Enter: ['Enter', 13], Escape: ['Escape', 27], Backspace: ['Backspace', 8] };
const key = (k, down) => {
  const [code, keyCode] = CODES[k] ?? [/^[a-z]$/i.test(k) ? `Key${k.toUpperCase()}` : '', k.toUpperCase().charCodeAt(0)];
  const init = { key: k, code, keyCode, which: keyCode, bubbles: true, cancelable: true };
  for (const t of [document.querySelector('canvas'), document, window]) t?.dispatchEvent(new KeyboardEvent(down ? 'keydown' : 'keyup', init));
};
const tap = async (k) => { document.querySelector('canvas')?.focus?.(); key(k, true); await new Promise((r) => setTimeout(r, 60)); key(k, false); await new Promise((r) => setTimeout(r, 60)); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const waitFor = async (test, ms, what) => {
  const t0 = performance.now();
  for (;;) { let v = false; try { v = test(); } catch { /* not yet */ } if (v) return performance.now() - t0; if (performance.now() - t0 > ms) throw new Error(`timed out after ${Math.round(ms / 1000)} s waiting for ${typeof what === 'function' ? what() : what}`); await sleep(100); }
};
const online = () => { const n = slot()?.net; return Boolean(n && (n.ready === true || n.status === 'online')); };

// Colour, name and emotes: the Unity player (its colour and emote ride in its pose) against the bot, both ways.
const hueOf = (hex) => {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(String(hex).slice(i, i + 2), 16) / 255);
  const mx = Math.max(r, g, b), d = mx - Math.min(r, g, b);
  if (!d) return 0;
  const h = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return ((h / 6) + 1) % 1;
};
const hueGap = (a, b) => { const d = Math.abs(hueOf(a) - hueOf(b)); return Math.min(d, 1 - d); };
const EMOTES = ['', 'wave', 'hop', 'spin'];
async function avatarChecks({ check, main, bot, until, wait, metric }) {
  const seenByBot = {}; // what the bot last saw from the Unity player
  bot.onPose((id, s) => { if (id === main.id) { seenByBot.c = s?.c; seenByBot.e = s?.e; seenByBot.n = s?.n; } });
  const shown = () => { const p = slot()?.poses?.[bot.id]; try { return p?.e ? JSON.parse(p.e) : null; } catch { return null; } };
  await check('Two players show different colours and names', async () => {
    await until(() => seenByBot.c && sentExtra?.c, 5000, 'the Unity player to send its colour');
    await until(() => hueGap(seenByBot.c, botStyle.c) >= 0.08, 5000, () => `the two colours to differ (Unity ${seenByBot.c}, bot ${botStyle.c})`);
    await until(() => shown()?.c === botStyle.c, 5000, 'the Unity page to receive the bot colour');
    const mine = bot.peers.get(main.id)?.name, theirs = main.peers.get(bot.id)?.name;
    if (!mine || !theirs || mine === theirs) throw new Error(`names must be set and differ (Unity player "${mine}", bot "${theirs}")`);
    metric('colourUnity', seenByBot.c); metric('colourBot', botStyle.c);
    return `${mine} ${seenByBot.c} / ${theirs} ${botStyle.c}`;
  });
  for (const k of [1, 2, 3]) {
    await check(`Emote ${k} (${EMOTES[k]}) plays for the other player, both ways`, async () => {
      // Unity player: a real key press on the page; the bot sees the emote arrive with the pose.
      const n0 = seenByBot.n ?? 0;
      document.querySelector('canvas')?.focus?.();
      key(String(k), true); await wait(80); key(String(k), false);
      await until(() => seenByBot.e === k && (seenByBot.n ?? 0) > n0, 5000, () => `the bot to see emote ${k} from the Unity player (saw e=${seenByBot.e}, n=${seenByBot.n})`);
      await wait(1100);
      // Bot: plays the same emote; the Unity page receives it and draws it on the bot's capsule.
      botStyle.e = k; botStyle.n += 1; const n = botStyle.n;
      bot.pose([3, 1, 3], 0);
      await until(() => shown()?.e === k && shown()?.n === n, 5000, () => `the Unity page to receive the bot's emote ${k} (it has ${JSON.stringify(shown())})`);
      await wait(1100); // long enough for the animation and its caption to show on the video
      return EMOTES[k];
    });
  }
}

const api = installNetPlaytest({
  product: 'hawkes-net-unity',
  where: endpoint().replace(/^https?:\/\//, ''),
  // The page is ready once Unity shows its start screen (nothing is joined before the player presses Join).
  ready: () => Boolean(globalThis.__hawkesNet) || startUp(),
  main,
  openBot: async () => {
    const { join } = await import(`${endpoint()}/sdk/hawkes-net@1.mjs`);
    const m = main();
    const b = await join(m.room, { project: 'netdemo', endpoint: endpoint(), transport: m.transport === 'p2p' ? 'auto' : 'relay', name: 'Playtest bot', role: 'player', sub: `pt-${Math.random()}`, log: false });
    // Every pose the bot makes carries its look, as the sample's players do.
    const pose = b.pose.bind(b);
    b.pose = (p, y, e) => pose(p, y, { ...botStyle, ...(e || {}) });
    return b;
  },
  extra: avatarChecks,
  drive: async (step) => {
    document.querySelector('canvas')?.focus?.();
    key('d', true); await step(1200); key('d', false);
    key('w', true); await step(900); key('w', false);
    await step(300);
  },
  myPos: () => sent ?? [0, 0, 0],
  // What the bridge hands the Unity side for that player (HawkesNetAvatars places its capsule from it each frame).
  shownPos: (id) => slot()?.poses?.[id]?.p ?? null,
  botPose: (u) => [-3 + 6 * u, 1, 3],
  tolerance: 0.3,
});

// ---------------------------------------------------------------------------------------------------------------------
// The start screen. The player's page opens on a start screen (name, room, Join), so every scenario first joins it with a
// real key press (Enter), and "start-screen" plays it through: name typed, Join, the status line going connecting ->
// online, then a failed join (the ticket request refused) with its reason, and joining again.
const baseStart = api.start.bind(api);
const joinFromStartScreen = async () => {
  await waitFor(() => online() || startUp(), 120000, 'Unity to show the start screen');
  const t0 = performance.now();
  while (!online() && performance.now() - t0 < 60000) { await tap('Enter'); await sleep(1500); }
};
const baseScenarios = api.scenarios.bind(api);
api.scenarios = () => [...baseScenarios(), { id: 'start-screen', title: 'Start screen: name, Join, status connecting/online, a failed join with its reason', seconds: 90, tags: ['full', 'ui'],
  description: 'Types a name on the start screen, presses Join and checks the status line goes connecting then online and the other players see the name; then refuses the ticket request, checks the status says Failed with the reason, and joins again.' }];

let sr = null; // the start-screen run
const r1 = (n) => Math.round(n * 10) / 10;
const srNow = () => (performance.now() - sr.t0) / 1000;
const srEvent = (type, detail) => sr.events.push({ t: r1(srNow()), type, ...(detail === undefined ? {} : { detail: String(detail) }) });
const srPanel = () => {
  let el = document.getElementById('hawkes-net-playtest');
  if (!el) { el = document.createElement('div'); el.id = 'hawkes-net-playtest'; el.style.cssText = 'position:fixed;right:12px;top:12px;z-index:99999;max-width:380px;background:rgba(10,12,16,.82);color:#e8eaed;font:12px/1.5 ui-monospace,Menlo,monospace;padding:8px 12px;border-radius:8px;pointer-events:none'; document.body.appendChild(el); }
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
  const head = sr.done ? (sr.state.status === 'won' ? '<b style="color:#9fe870">PASS</b>' : '<b style="color:#ff7b72">FAIL</b>') : '<b style="color:#ffd84d">running</b>';
  el.innerHTML = `Playtest start-screen · ${head}<br>` + sr.lines.map((l) => `${l.ok === true ? '<span style="color:#9fe870">✓</span>' : l.ok === false ? '<span style="color:#ff7b72">✗</span>' : '…'} ${esc(l.text)}${l.why ? ` <span style="color:#9aa1ad">(${esc(l.why)})</span>` : ''}`).join('<br>');
};
async function srCheck(text, fn) {
  const line = { text, ok: null, why: '' };
  sr.lines.push(line); srPanel(); srEvent('check', text);
  try { const why = await fn(); line.ok = true; line.why = typeof why === 'string' ? why : ''; srEvent('pass', text); sr.metrics.checksPassed = (sr.metrics.checksPassed ?? 0) + 1; }
  catch (e) { line.ok = false; line.why = String(e?.message ?? e).slice(0, 220); sr.problems.push({ severity: 'bug', message: `${text}: ${line.why}` }); srEvent('fail', text); sr.metrics.checksFailed = (sr.metrics.checksFailed ?? 0) + 1; }
  srPanel();
  return line.ok;
}
const statusLines = (from) => startLog.filter((l) => l.at >= from).map((l) => l.text);

async function startScreen() {
  const typed = 'Ada Bot';
  let defaultName = '';
  if (!await srCheck('The start screen is up before anyone has joined', async () => {
    await waitFor(startUp, 120000, 'Unity to show the start screen (no "[HawkesNetStart] start screen up" in the console)');
    if (online()) throw new Error('the page had already joined a room without the player pressing Join');
    defaultName = /name (.+), room/.exec(startLog.find((l) => l.text.startsWith('start screen up')).text)?.[1] ?? '';
    srEvent('log', `start screen up, prefilled name "${defaultName}"`);
    return `prefilled name "${defaultName}"`;
  })) return;
  sr.progress = 0.15;

  let name = typed;
  await srCheck('A name is typed into the start screen', async () => {
    // Real key presses into the name field (focused when the screen shows): clear the prefilled name, type a new one.
    for (let i = 0; i < Math.max(30, defaultName.length + 5); i++) await tap('Backspace');
    for (const ch of typed) { if (ch === ' ') { await tap(' '); continue; } await tap(ch); }
    srEvent('typed', typed);
    return `typed "${typed}"`;
  });
  sr.progress = 0.3;

  const mark = performance.now();
  if (!await srCheck('Pressing Join connects: the status says connecting, then online', async () => {
    await tap('Enter');
    const ms = await waitFor(online, 30000, () => `the room to come online (status lines: ${statusLines(mark).join(' / ') || 'none'})`);
    const lines = statusLines(mark);
    const iC = lines.findIndex((t) => t.startsWith('connecting')), iO = lines.findIndex((t) => t.startsWith('online'));
    if (iC < 0) throw new Error(`no "connecting" status was shown (lines: ${lines.join(' / ')})`);
    if (iO < iC) throw new Error(`"online" did not follow "connecting" (lines: ${lines.join(' / ')})`);
    sr.metrics.joinMs = Math.round(ms);
    srEvent('status', lines.join(' -> '));
    return lines.join(' -> ');
  })) return;
  sr.progress = 0.5;

  await srCheck('The room knows the player by the name from the start screen', async () => {
    const net = slot().net;
    await waitFor(() => net.peers?.get(net.id), 10000, 'the room to list this player');
    const got = net.peers.get(net.id)?.name ?? net.peers.get(net.id);
    // Typing through scripted key events is accepted by some browsers' canvases and not others: when the typed name did
    // not arrive, the prefilled name must have, and that is said as a warning (the page took a name, but not ours).
    if (got === typed) { name = typed; return `"${got}"`; }
    if (got && got === defaultName) { sr.problems.push({ severity: 'warning', message: `the typed name did not reach the field (the player joined as the prefilled "${got}")` }); return `joined as the prefilled "${got}" (typing was not accepted)`; }
    throw new Error(`expected "${typed}" (or the prefilled "${defaultName}"), the room has "${got}"`);
  });
  sr.progress = 0.65;

  // A failed join: leave (Esc), have the ticket request refused, press Join again.
  const realFetch = globalThis.fetch;
  let refused = 0;
  const mark2 = performance.now();
  try {
    if (!await srCheck('A failed join says Failed, with the reason', async () => {
      globalThis.fetch = (input, init) => {
        const url = String(input?.url ?? input);
        if (/\/v1\/net\/ticket/.test(url)) { refused++; return Promise.resolve(new Response(JSON.stringify({ error: 'forbidden' }), { status: 403, headers: { 'Content-Type': 'application/json' } })); }
        return realFetch(input, init);
      };
      await tap('Escape');
      await waitFor(() => statusLines(mark2).some((t) => t.startsWith('off |')), 10000, 'the start screen to come back after Esc (status off)');
      await tap('Enter');
      await waitFor(() => statusLines(mark2).some((t) => t.startsWith('offline')), 30000, () => `a Failed status (lines: ${statusLines(mark2).join(' / ')}; ticket requests refused: ${refused})`);
      const failed = statusLines(mark2).find((t) => t.startsWith('offline'));
      if (!/Failed: .+\(.+\)/.test(failed)) throw new Error(`the failed status has no reason: ${failed}`);
      sr.metrics.refusedTicketRequests = refused;
      srEvent('status', failed);
      return failed.replace(/^offline \| /, '');
    })) return;
    sr.progress = 0.85;
  } finally { globalThis.fetch = realFetch; }

  await srCheck('Try again joins once the refusal is gone, with the same name', async () => {
    const mark3 = performance.now();
    await tap('Enter');
    await waitFor(online, 40000, () => `the room to come online again (lines: ${statusLines(mark3).join(' / ')})`);
    const net = slot().net;
    const got = net.peers?.get(net.id)?.name ?? net.peers?.get(net.id);
    if (got !== name && got !== defaultName) throw new Error(`the name changed on the way (${got})`);
    return `online as "${got}"`;
  });
  sr.progress = 1;
}

api.start = async (id, options = {}) => {
  if (id !== 'start-screen') {
    try { await joinFromStartScreen(); } catch (e) { /* the scenario's own connect check says what is missing */ }
    return baseStart(id, options);
  }
  if (sr && !sr.done) sr.done = true;
  sr = { t0: performance.now(), done: false, lines: [], events: [], problems: [], metrics: {}, progress: 0, state: { ready: true, status: 'running', progress: 0, scene: 'Demo' } };
  const mine = sr;
  srEvent('start', id); srEvent('scene', 'Demo'); srPanel();
  try { await startScreen(); } catch (e) { sr.problems.push({ severity: 'bug', message: `bot error: ${e?.message ?? e}` }); }
  mine.done = true; mine.seconds = srNow();
  mine.state.status = mine.lines.length && mine.lines.every((l) => l.ok) ? 'won' : 'lost';
  mine.state.note = mine.state.status === 'won' ? 'all checks passed' : `${mine.lines.filter((l) => l.ok === false).length} check(s) failed`;
  srEvent('finish', mine.state.status); srPanel();
};
const baseState = api.state.bind(api), baseReport = api.report.bind(api), baseStop = api.stop.bind(api);
api.state = () => (sr && !sr.done ? { ...sr.state, progress: sr.progress, metrics: { ...sr.metrics } }
  : sr && api.__last === 'start-screen' ? { ...sr.state, progress: sr.progress, ready: true, metrics: { ...sr.metrics } } : baseState());
api.report = () => (sr && api.__last === 'start-screen' ? { scenario: 'start-screen', outcome: sr.done ? sr.state.status : 'stuck', seconds: r1(sr.seconds ?? srNow()), metrics: { ...sr.metrics },
  events: sr.events.slice(), problems: sr.problems.slice(), log: [...sr.lines.map((l) => `${l.ok ? 'PASS' : l.ok === false ? 'FAIL' : '...'} ${l.text}${l.why ? ` (${l.why})` : ''}`), ...startLog.map((l) => `console ${l.text}`)] } : baseReport());
api.stop = () => { if (sr && !sr.done) { sr.done = true; sr.state.status = 'done'; } baseStop(); };
// Which scenario was started last decides what state() and report() answer.
const startAny = api.start;
api.start = (id, options) => { api.__last = id; return startAny(id, options); };
