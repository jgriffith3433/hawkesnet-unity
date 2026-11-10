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
let sentYaw = 0; // which way the Unity player faces (radians, Unity's yaw: 0 is +z)
let sentExtra = null; // the extra values (colour, emote) the Unity player sent with its newest pose
// The second player's look, sent with every pose it makes: an orange capsule, no emote yet (the sample's Style).
const botStyle = { c: '#ff9d2e', e: 0, n: 0 };
const main = () => {
  const s = slot();
  const net = s?.net ?? null;
  // What the Unity player sends (HawkesNet_Pose calls net.pose): its own place, as the other player should see it.
  if (net && !net.__hnptPose) {
    const pose = net.pose.bind(net);
    net.pose = (p, y, e) => { sent = Array.from(p).slice(0, 3); sentYaw = Number(y) || 0; sentExtra = e && typeof e === 'object' ? { ...e } : null; return pose(p, y, e); };
    net.__hnptPose = true;
  }
  return net;
};
// Unity reads the keyboard from the page: a press on the canvas and the window, with the key's code as a browser sends it.
const CODES = { d: ['KeyD', 68], w: ['KeyW', 87], 1: ['Digit1', 49], 2: ['Digit2', 50], 3: ['Digit3', 51],
  Enter: ['Enter', 13], F3: ['F3', 114], Escape: ['Escape', 27], Backspace: ['Backspace', 8] };
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
// Same rule as the Avatars sample: when two hues sit within 0.08, the id that sorts last picks the free hue farthest from the rest.
const hsvHex = (h, s = 0.65, v = 0.95) => {
  const i = Math.floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
  const [r, g, b] = [[v, t, p], [q, v, p], [p, v, t], [p, q, v], [t, p, v], [v, p, q]][i % 6];
  return '#' + [r, g, b].map((x) => Math.round(x * 255).toString(16).padStart(2, '0')).join('');
};
const farColour = (...hexes) => {
  const avoids = hexes.filter(Boolean).map(hueOf);
  let best = 0, bestGap = -1;
  for (let i = 0; i < 24; i++) {
    const h = i / 24;
    let gap = 1;
    for (const o of avoids) gap = Math.min(gap, Math.min(Math.abs(h - o), 1 - Math.abs(h - o)));
    if (gap > bestGap) { bestGap = gap; best = h; }
  }
  return hsvHex(best);
};
const EMOTES = ['', 'wave', 'hop', 'spin'];
async function avatarChecks({ check, main, bot, until, wait, metric }) {
  const seenByBot = {}; // what the bot last saw from the Unity player
  bot.onPose((id, s) => { if (id === main.id) { seenByBot.c = s?.c; seenByBot.e = s?.e; seenByBot.n = s?.n; } });
  const shown = () => { const p = slot()?.poses?.[bot.id]; try { return p?.e ? JSON.parse(p.e) : null; } catch { return null; } };
  await check('Two players show different colours and names', async () => {
    await until(() => seenByBot.c && sentExtra?.c, 5000, 'the Unity player to send its colour');
    // The bot used to keep a fixed orange (#ff9d2e). ~16% of random Unity hues sit within 0.08 of that, and the sample
    // only moves the later-sorted id — so when Unity's id sorted first the check timed out on two colours that looked
    // different on screen (e.g. #F26455 vs #ff9d2e, hue gap ~0.073). Resolve like the sample when we sort last.
    if (hueGap(seenByBot.c, botStyle.c) < 0.08 && bot.id > main.id) {
      botStyle.c = farColour(seenByBot.c);
      bot.pose([3, 1, 3], 0);
    }
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

// The second player, in the Unity player's room: what the bot is in every scenario.
const openBotPlayer = async () => {
  const { join } = await import(`${endpoint()}/sdk/hawkes-net@1.mjs`);
  const m = main();
  // Prefer a colour already far from the Unity player's before the first pose, so the capsules look distinct from join.
  if (sentExtra?.c && hueGap(sentExtra.c, botStyle.c) < 0.08) botStyle.c = farColour(sentExtra.c);
  const b = await join(m.room, { project: 'netdemo', endpoint: endpoint(), transport: m.transport === 'p2p' ? 'auto' : 'relay', name: 'Playtest bot', role: 'player', sub: `pt-${Math.random()}`, log: false });
  // Every pose the bot makes carries its look, as the sample's players do.
  const pose = b.pose.bind(b);
  b.pose = (p, y, e) => pose(p, y, { ...botStyle, ...(e || {}) });
  return b;
};

const api = installNetPlaytest({
  product: 'hawkes-net-unity',
  where: endpoint().replace(/^https?:\/\//, ''),
  // The page is ready once Unity shows its start screen (nothing is joined before the player presses Join).
  ready: () => Boolean(globalThis.__hawkesNet) || startUp(),
  main,
  openBot: openBotPlayer,
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
  // Enter first (name field focused); every few tries also a real click where the Join button is (centre column, about
  // half way down the 960x600 start form), for a page where the key does not reach the field.
  for (let i = 0; !online() && performance.now() - t0 < 60000; i++) {
    await tap('Enter');
    if (i % 3 === 2) await clickJoin();
    await sleep(1500);
  }
  if (!online() && !slot()) throw new Error('the Join form was still up after 60 s of Enter and clicks on Join: no client was made, because Join was never accepted (not because the server is unreachable)');
};
const clickJoin = async () => {
  const c = document.querySelector('canvas');
  if (!c) return;
  const r = c.getBoundingClientRect();
  // The Join button sits at y 236-272 of the 960x600 canvas (248 is on it with the room line shown or not); the same
  // spot is Cancel while connecting, so callers only click before any status line has appeared.
  for (const fx of [0.5, 0.667]) {
    const init = { bubbles: true, cancelable: true, clientX: r.left + r.width * fx, clientY: r.top + r.height * (248 / 600), button: 0, view: window };
    for (const [t, b] of [['mousemove', 0], ['mousedown', 1], ['mouseup', 0], ['click', 0]]) { c.dispatchEvent(new MouseEvent(t, { ...init, buttons: b })); await sleep(60); }
    await sleep(150);
  }
};
// ---------------------------------------------------------------------------------------------------------------------
// Fingers. Unity reads touches from the canvas's touch events, so the phone scenario sends the events a finger makes
// (touchstart, touchmove, touchend with a real Touch each): nothing reaches into the game. Where the buttons and the joystick
// are comes from the game itself (HawkesNetStartScreen and HawkesNetTouchControls tell window.__hawkesNet.touch), in
// fractions of the canvas.
const touchState = () => globalThis.__hawkesNet?.touch ?? null;
const canvasAt = (fx, fy) => { const r = document.querySelector('canvas').getBoundingClientRect(); return { x: r.left + r.width * fx, y: r.top + r.height * fy }; };
let fingerId = 100;
const finger = (type, id, x, y) => {
  const c = document.querySelector('canvas');
  if (typeof Touch !== 'function' || typeof TouchEvent !== 'function') throw new Error('this browser has no touch events (Touch/TouchEvent): a phone playtest needs touch emulation');
  const t = new Touch({ identifier: id, target: c, clientX: x, clientY: y, pageX: x + scrollX, pageY: y + scrollY, screenX: x, screenY: y, radiusX: 8, radiusY: 8, force: 1 });
  const live = type === 'touchend' || type === 'touchcancel' ? [] : [t];
  c.dispatchEvent(new TouchEvent(type, { touches: live, targetTouches: live, changedTouches: [t], bubbles: true, cancelable: true, view: window }));
};
const tapAt = async (x, y, hold = 90) => { const id = fingerId++; finger('touchstart', id, x, y); await sleep(hold); finger('touchend', id, x, y); await sleep(120); };
const tapFraction = (fx, fy) => { const p = canvasAt(fx, fy); return tapAt(p.x, p.y); };
const formButton = (name) => { const b = touchState()?.form?.[name]; if (!b) throw new Error(`the game does not show a "${name}" button (window.__hawkesNet.touch.form has ${Object.keys(touchState()?.form ?? {}).join(', ') || 'nothing'})`); return b; };
const tapForm = (name) => { const b = formButton(name); return tapFraction(b.x + b.w / 2, b.y + b.h / 2); };
// Puts a thumb down at (fx, fy), drags it by (dx, dy) fractions of the canvas height in a few steps, holds, and lets go.
async function drag(from, dx, dy, holdMs, onHold) {
  const c = document.querySelector('canvas'), r = c.getBoundingClientRect(), id = fingerId++;
  const x0 = r.left + r.width * from.x, y0 = r.top + r.height * from.y, px = dx * r.height, py = dy * r.height;
  finger('touchstart', id, x0, y0); await sleep(80);
  for (let i = 1; i <= 6; i++) { finger('touchmove', id, x0 + px * i / 6, y0 + py * i / 6); await sleep(40); }
  const t0 = performance.now();
  while (performance.now() - t0 < holdMs) { finger('touchmove', id, x0 + px, y0 + py); await onHold?.(); await sleep(60); }
  finger('touchend', id, x0 + px, y0 + py); await sleep(150);
}
// The phone's join form: types into the page's own text box (the one the phone keyboard raises), like a player does.
async function typeIntoBox(text) {
  await waitFor(() => document.getElementById('hawkes-net-text-input'), 5000, 'the text box for the phone keyboard to open after tapping the field');
  const input = document.getElementById('hawkes-net-text-input');
  input.focus(); input.value = '';
  input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'deleteContentBackward' }));
  for (const ch of text) { input.value += ch; input.dispatchEvent(new InputEvent('input', { bubbles: true, data: ch, inputType: 'insertText' })); await sleep(40); }
  await sleep(250);
  document.getElementById('hawkes-net-text-ok').click();
  await waitFor(() => !document.getElementById('hawkes-net-text'), 3000, 'the text box to close on OK');
}

async function phone() {
  const c = document.querySelector('canvas');
  const dpr = window.devicePixelRatio || 1;
  // 1. The page: the canvas is the screen.
  if (!await srCheck('At phone size the canvas fills the screen with no sideways scroll', async () => {
    await waitFor(startUp, 120000, 'Unity to show the start screen');
    const r = c.getBoundingClientRect(), de = document.documentElement;
    const w = innerWidth, h = innerHeight;
    sr.metrics.viewport = `${w}x${h}@${dpr}`; sr.metrics.canvasCss = `${Math.round(r.width)}x${Math.round(r.height)}`; sr.metrics.canvasPx = `${c.width}x${c.height}`;
    srEvent('view', `viewport ${w}x${h} at ${dpr}x, canvas ${Math.round(r.width)}x${Math.round(r.height)} css px (${c.width}x${c.height} drawn)`);
    if (w > 700) sr.problems.push({ severity: 'warning', message: `the page is ${w} px wide, not phone size: the phone checks ran at this size` });
    if (Math.abs(r.left) > 1 || Math.abs(r.top) > 1 || Math.abs(r.width - w) > 1.5 || Math.abs(r.height - h) > 1.5) throw new Error(`the canvas is ${Math.round(r.width)}x${Math.round(r.height)} at ${Math.round(r.left)},${Math.round(r.top)} on a ${w}x${h} screen`);
    if (de.scrollWidth > w + 1 || document.body.scrollWidth > w + 1) throw new Error(`the page is wider than the screen (${Math.max(de.scrollWidth, document.body.scrollWidth)} px of ${w})`);
    if (de.scrollHeight > h + 1) throw new Error(`the page is taller than the screen (${de.scrollHeight} px of ${h})`);
    scrollTo(300, 0); await sleep(100);
    const moved = scrollX; scrollTo(0, 0);
    if (moved > 0) throw new Error(`the page scrolled sideways by ${moved} px`);
    const want = r.width * dpr;
    if (Math.abs(c.width - want) > Math.max(2, want * 0.02)) throw new Error(`the canvas is drawn ${c.width} px wide for ${Math.round(want)} px of screen (devicePixelRatio ${dpr}): blurry or too heavy`);
    return `${Math.round(r.width)}x${Math.round(r.height)} css px, drawn ${c.width}x${c.height} at ${dpr}x, no scroll`;
  })) return;
  sr.progress = 0.15;

  // 2. The Join form by touch: tap the name, type with the phone's text box, tap Join.
  const typed = 'Phone Ada';
  const mark = performance.now();
  if (!await srCheck('The Join form can be filled and submitted by touch', async () => {
    await waitFor(() => touchState()?.form?.join, 20000, 'the Join button to be shown (the game reports its buttons)');
    await tapForm('name');
    srEvent('tap', 'the name field');
    await typeIntoBox(typed);
    srEvent('typed', typed);
    await tapForm('join');
    srEvent('tap', 'Join');
    const ms = await waitFor(online, 40000, () => `the room to come online after tapping Join (status lines: ${statusLines(mark).join(' / ') || 'none'})`);
    sr.metrics.joinMs = Math.round(ms);
    const net = slot().net;
    await waitFor(() => net.peers?.get(net.id), 10000, 'the room to list this player');
    const got = net.peers.get(net.id)?.name ?? net.peers.get(net.id);
    if (got !== typed) throw new Error(`joined as "${got}", not the "${typed}" typed in the box`);
    return `typed "${typed}", tapped Join, ${statusLines(mark).join(' -> ')}`;
  })) return;
  sr.progress = 0.4;

  const net = slot().net;
  main(); // wraps the pose call, so what the Unity player sends is seen
  await waitFor(() => sent, 5000, 'the Unity player to send its place');
  await waitFor(() => touchState()?.stick, 5000, 'the game to report its joystick');

  // 3. The joystick: a thumb on the left, drag right then up; the capsule must go there.
  await srCheck('A touch joystick moves the avatar', async () => {
    const t = touchState();
    if (!t.active) throw new Error('the on-screen controls are not shown on this touch screen');
    const from = { x: t.stick.x, y: t.stick.y };
    // Drag by 0.9 of the stick's radius (drag() takes fractions of the canvas height both ways).
    const a = sent.slice();
    let heldSeen = false, moveSeen = 0;
    srEvent('touch', 'thumb on the joystick, dragging right');
    await drag(from, t.stick.r * 0.9, 0, 1600, async () => { const s = touchState(); if (s.holding) heldSeen = true; moveSeen = Math.max(moveSeen, Math.hypot(...(s.move ?? [0, 0]))); });
    const b = sent.slice();
    const dx = b[0] - a[0];
    srEvent('moved', `right: ${dx.toFixed(1)} m`);
    if (!heldSeen) throw new Error('the game never saw a thumb on the joystick');
    if (dx < 1.5) throw new Error(`dragging right moved the avatar only ${dx.toFixed(2)} m east (stick read ${moveSeen.toFixed(2)})`);
    srEvent('touch', 'dragging up the screen');
    await drag(from, 0, -t.stick.r * 0.9, 1600);
    const d = sent.slice(), dz = d[2] - b[2];
    srEvent('moved', `up: ${dz.toFixed(1)} m`);
    if (dz < 1.5) throw new Error(`dragging up moved the avatar only ${dz.toFixed(2)} m north`);
    await sleep(400);
    const e1 = sent.slice(); await sleep(500); const e2 = sent.slice();
    if (touchState().holding || Math.hypot(e2[0] - e1[0], e2[2] - e1[2]) > 0.05) throw new Error('the avatar kept walking after the thumb was lifted');
    sr.metrics.joystickEastM = Math.round(dx * 10) / 10; sr.metrics.joystickNorthM = Math.round(dz * 10) / 10;
    return `right ${dx.toFixed(1)} m, up ${dz.toFixed(1)} m, stopped when let go`;
  });
  sr.progress = 0.7;

  // 4. The emote buttons: a second player is in the room and must see each emote.
  const bot = smoothBot = await openBotPlayer();
  await waitFor(() => (bot.ready === true || bot.status === 'online') && net.peers.has(bot.id), 15000, 'the second player to join the room');
  const seen = {};
  bot.onPose((id, st) => { if (id === net.id) { seen.e = st?.e; seen.n = st?.n; } });
  bot.pose([3, 1, 3], 0);
  await sleep(600);
  await srCheck('Emote buttons on screen play emotes the other player sees', async () => {
    const out = [];
    for (const k of [1, 2, 3]) {
      const b = touchState().emotes.find((x) => x.n === k);
      if (!b) throw new Error(`no on-screen button for emote ${k}`);
      const n0 = seen.n ?? 0, taps0 = touchState().taps ?? 0;
      srEvent('tap', `emote button ${k} (${EMOTES[k]})`);
      await tapFraction(b.x, b.y);
      await waitFor(() => seen.e === k && (seen.n ?? 0) > n0, 5000, () => `the second player to see emote ${k} after the tap (saw e=${seen.e}, n=${seen.n}; the game counted ${touchState().taps} taps)`);
      if ((touchState().taps ?? 0) <= taps0) throw new Error(`the game did not count the tap on button ${k}`);
      out.push(EMOTES[k]);
      await sleep(1300); // the animation and its caption on the footage
    }
    return `the other player saw ${out.join(', ')}`;
  });
  sr.progress = 1;
}

const baseScenarios = api.scenarios.bind(api);
api.scenarios = () => [...baseScenarios(), { id: 'phone', title: 'Phone: the canvas fills the screen, join by touch, touch joystick, emote buttons', seconds: 90, tags: ['full', 'ui', 'phone'],
  description: 'Meant for a phone-size page with touch (390x844). Checks the canvas is the whole viewport with no sideways scroll and is drawn at the screen\'s pixel ratio; taps the name field, types in the phone text box and taps Join; drags the on-screen joystick right and up and checks the avatar walked there and stopped when the thumb lifted; then taps the three emote buttons and checks a second player sees each emote. All with touch events on the canvas.' },
{ id: 'start-screen', title: 'Start screen: name, Join, status connecting/online, a failed join with its reason', seconds: 90, tags: ['full', 'ui'],
  description: 'Types a name on the start screen, presses Join and checks the status line goes connecting then online and the other players see the name; then refuses the ticket request, checks the status says Failed with the reason, and joins again.' },
{ id: 'smooth', title: 'Smooth remote avatar: moves every frame, no snapping at 150 ms latency, prediction capped at 250 ms then holds, Latency HUD and F3 debug line', seconds: 75, tags: ['full', 'netcode'],
  description: 'Joins from the start screen, opens a second player that walks a figure eight in front of the camera, and reads where Unity draws it every frame (window.__hawkesNet.watch): 6 s on the plain connection (it must move every frame, facing where it goes), then 10 s with 150 ms (+0-30 ms jitter) of simulated latency on the poses (no frame may step faster than twice its top speed or turn more than twice its top turn rate). Then it checks the HUD (plain "Latency: N ms"; the detail line only after F3), and stops the bot\'s poses for 3 s: the prediction must never pass 250 ms and the avatar must hold still.' },
{ id: 'crowd', title: 'Two avatars never share a spot, and a 30 s walk keeps the avatar on the ground inside the edge walls', seconds: 90, tags: ['full', 'avatars'],
  description: 'Joins from the start screen and opens a second player. Walks the Unity player (real key presses) straight at the bot and keeps pushing: it must stay apart (never on the same centre), and settle at least 0.55 m away once the keys are let go. Then the bot is put exactly on the Unity player\'s spot (a setup shortcut): the Unity player must ease apart to 0.55 m or more. Then the bot goes far away and the player walks in every direction for 30 s (d, s, a, w, then diagonals): every pose it sends must stay on the ground (y = 1) and inside the arena (the edge walls), and it must reach an edge.' },
{ id: 'page', title: 'Loading screen (progress, fade, phone fit), playtest hook while loading, demo-root.json, load error card', seconds: 30, tags: ['smoke', 'ui'],
  description: 'Checks the inline stub put window.hawkesPlaytest up before Unity finished (within 1 s of page start), that ./demo-root.json answers so a deeper path can relocate here, and that a load failure shows the plain error card (via the page helper, same card ?failLoad=1 uses).' }];
// The scenarios played here rather than by net-playtest.mjs: their own run, panel and report.
const SIDE = ['start-screen', 'smooth', 'crowd', 'phone', 'page'];

let sr = null; // the start-screen run
const r1 = (n) => Math.round(n * 10) / 10;
const srNow = () => (performance.now() - sr.t0) / 1000;
const srEvent = (type, detail) => sr.events.push({ t: r1(srNow()), type, ...(detail === undefined ? {} : { detail: String(detail) }) });
const srPanel = () => {
  let el = document.getElementById('hawkes-net-playtest');
  if (!el) { el = document.createElement('div'); el.id = 'hawkes-net-playtest'; el.style.cssText = 'position:fixed;right:12px;top:12px;z-index:99999;max-width:380px;background:rgba(10,12,16,.82);color:#e8eaed;font:12px/1.5 ui-monospace,Menlo,monospace;padding:8px 12px;border-radius:8px;pointer-events:none'; document.body.appendChild(el); }
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
  const head = sr.done ? (sr.state.status === 'won' ? '<b style="color:#9fe870">PASS</b>' : '<b style="color:#ff7b72">FAIL</b>') : '<b style="color:#ffd84d">running</b>';
  el.innerHTML = `Playtest ${esc(sr.id)} · ${head}<br>` + sr.lines.map((l) => `${l.ok === true ? '<span style="color:#9fe870">✓</span>' : l.ok === false ? '<span style="color:#ff7b72">✗</span>' : '…'} ${esc(l.text)}${l.why ? ` <span style="color:#9aa1ad">(${esc(l.why)})</span>` : ''}`).join('<br>');
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
    const version = /version (\d+\.\d+\.\d+)/.exec(startLog.find((l) => l.text.startsWith('start screen up')).text)?.[1];
    if (!version) throw new Error('the Join screen shows no package version ("Hawkes Net for Unity v<x.y.z>")');
    srEvent('log', `start screen up, prefilled name "${defaultName}", Join screen shows Hawkes Net for Unity v${version}`);
    return `prefilled name "${defaultName}", Join screen shows v${version}`;
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
    // Enter, and every few tries a real click on Join, until the lobby says anything (connecting / offline): one scripted
    // Enter alone was not always taken, and the screen then sat there silent.
    for (let i = 0; i < 8 && !online() && statusLines(mark).length === 0; i++) {
      await tap('Enter');
      if (i % 2 === 1) await clickJoin();
      await sleep(1200);
    }
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

// ---------------------------------------------------------------------------------------------------------------------
// "smooth": the other player's capsule as Unity draws it, frame by frame. HawkesNetAvatars tells the page where it drew
// each remote every frame while window.__hawkesNet.watch is on (HawkesNet.jslib keeps them in window.__hawkesNet.frames),
// so this checks the picture itself, not the poses the bridge received.
const wrapDeg = (d) => ((d % 360) + 540) % 360 - 180;
// The bot's walk: a figure eight 6 m wide and 3 m deep, 5 m in front of the Unity player, facing where it goes.
const EIGHT_A = 3, EIGHT_W = 0.8;
const eight = (ms, base, yaw0) => {
  const t = (ms / 1000) * EIGHT_W;
  const lx = EIGHT_A * Math.sin(t), lz = EIGHT_A * Math.sin(t) * Math.cos(t), dx = EIGHT_A * Math.cos(t), dz = EIGHT_A * Math.cos(2 * t);
  const c = Math.cos(yaw0), sn = Math.sin(yaw0); // Unity's yaw turns +z towards +x
  const ax = lx * c + (lz + 5) * sn, az = -lx * sn + (lz + 5) * c;
  return { p: [base[0] + ax, base[1], base[2] + az], yaw: Math.atan2(dx, dz) + yaw0 };
};
// Its top speed (m/s) and turn rate (degrees a second), from the path itself.
const eightLimits = (() => {
  let speed = 0, turn = 0;
  for (let ms = 0; ms < 8000; ms += 5) {
    const a = eight(ms, [0, 0, 0], 0), b = eight(ms + 5, [0, 0, 0], 0);
    speed = Math.max(speed, Math.hypot(b.p[0] - a.p[0], b.p[2] - a.p[2]) / 0.005);
    turn = Math.max(turn, Math.abs(wrapDeg((b.yaw - a.yaw) * 180 / Math.PI)) / 0.005);
  }
  return { speed, turn };
})();

// What the frames say about one remote: frames seen, frames it did not move, steps that jump, how it faces.
function framesOf(frames, id) {
  const rows = frames.map((f) => ({ f: f.f, t: f.t, dt: Number(f.dt) || 16.7, it: (f.items || []).find((i) => i.id === id) })).filter((r) => r.it);
  const out = { frames: 0, still: 0, snaps: 0, turnSnaps: 0, skipped: 0, topSpeed: 0, topTurn: 0, worstStep: '', fps: 0, delays: [], aheadFrames: 0, maxAhead: 0, facing: [] };
  for (let i = 1; i < rows.length; i++) {
    const a = rows[i - 1], b = rows[i];
    if (b.f !== a.f + 1) { out.skipped++; continue; }
    const dt = Math.max(1, b.dt), step = Math.hypot(b.it.p[0] - a.it.p[0], b.it.p[1] - a.it.p[1], b.it.p[2] - a.it.p[2]);
    const turn = Math.abs(wrapDeg(b.it.y - a.it.y));
    out.frames++;
    if (step < 1e-4 && turn < 0.01) out.still++;
    const speed = step / dt * 1000, rate = turn / dt * 1000;
    if (step > Math.max(0.05, eightLimits.speed * 2 * dt / 1000)) { out.snaps++; out.worstStep = `${step.toFixed(2)} m in ${dt.toFixed(0)} ms at frame ${b.f}`; }
    if (turn > Math.max(4, eightLimits.turn * 2 * dt / 1000)) { out.turnSnaps++; out.worstStep = `${turn.toFixed(1)} degrees in ${dt.toFixed(0)} ms at frame ${b.f}`; }
    out.topSpeed = Math.max(out.topSpeed, speed); out.topTurn = Math.max(out.topTurn, rate);
    out.delays.push(Number(b.it.d) || 0);
    if ((Number(b.it.a) || 0) > 0) { out.aheadFrames++; out.maxAhead = Math.max(out.maxAhead, Number(b.it.a)); }
    // Facing: the drawn yaw against the direction the drawn capsule is going.
    if (speed > 0.5) out.facing.push(Math.abs(wrapDeg(b.it.y - Math.atan2(b.it.p[0] - a.it.p[0], b.it.p[2] - a.it.p[2]) * 180 / Math.PI)));
  }
  if (rows.length > 1) out.fps = (rows[rows.length - 1].f - rows[0].f) / Math.max(0.001, (rows[rows.length - 1].t - rows[0].t) / 1000);
  const med = (xs) => { const s = [...xs].sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : 0; };
  out.delay = med(out.delays); out.facingErr = med(out.facing);
  return out;
}

let smoothBot = null, smoothTick = null;
async function smooth() {
  const S = () => globalThis.__hawkesNet;
  if (!await srCheck('Connected to Hawkes Net from the start screen', async () => {
    await joinFromStartScreen();
    if (!online()) throw new Error('the room did not come online after pressing Join');
    return slot().net.roomName ?? slot().net.room ?? 'online';
  })) return;
  sr.progress = 0.1;
  const net = slot().net;
  if (!await srCheck('A second player joined the same room', async () => {
    smoothBot = await openBotPlayer();
    const ms = await waitFor(() => (smoothBot.ready === true || smoothBot.status === 'online') && net.peers.has(smoothBot.id), 15000, 'both players to see each other');
    sr.metrics.secondJoinMs = Math.round(ms);
    return `${Math.round(ms)} ms`;
  })) return;
  sr.progress = 0.2;
  const id = smoothBot.id;
  // The bot walks its figure eight, a pose each 16 ms (the SDK sends the newest about 30 times a second).
  await waitFor(() => sent, 5000, 'the Unity player to send its place');
  const base = sent.slice(), yaw0 = sentYaw, t0 = performance.now();
  srEvent('walk', `figure eight 5 m ahead of the Unity player: top ${eightLimits.speed.toFixed(1)} m/s, ${eightLimits.turn.toFixed(0)} deg/s`);
  smoothTick = setInterval(() => { try { const w = eight(performance.now() - t0, base, yaw0); smoothBot.pose(w.p, w.yaw); } catch { /* left */ } }, 16);
  S().watch = true;
  await waitFor(() => (S().frames || []).some((f) => (f.items || []).some((i) => i.id === id)), 10000, 'Unity to draw the second player');
  await sleep(1500); // the track learns the connection
  sr.progress = 0.3;

  // 1. The plain connection: every frame moves, facing where it goes.
  S().frames = [];
  srEvent('phase', 'plain connection, 6 s');
  await sleep(6000);
  const a = framesOf(S().frames, id);
  Object.assign(sr.metrics, { fps: Math.round(a.fps), framesPlain: a.frames, stillFramesPlain: a.still, snapsPlain: a.snaps + a.turnSnaps, delayPlainMs: Math.round(a.delay), facingErrDeg: Math.round(a.facingErr * 10) / 10 });
  await srCheck(`The other player's avatar moves every frame (${Math.round(a.fps)} fps)`, async () => {
    if (a.frames < 120) throw new Error(`only ${a.frames} frames were drawn in 6 s`);
    if (a.still > 0) throw new Error(`it stood still in ${a.still} of ${a.frames} frames while the player walked`);
    if (a.fps < 50) sr.problems.push({ severity: 'warning', message: `the page ran at ${Math.round(a.fps)} fps (the criterion is 60): the avatar moved in all of those frames` });
    return `moved in all ${a.frames} frames at ${Math.round(a.fps)} fps, ${Math.round(a.delay)} ms behind`;
  });
  await srCheck('It faces where it walks (look direction)', async () => {
    if (a.facing.length < 60) throw new Error(`too few moving frames to tell (${a.facing.length})`);
    if (a.facingErr > 15) throw new Error(`its facing is ${a.facingErr.toFixed(1)} degrees off its path (median)`);
    if (a.snaps + a.turnSnaps > 0) throw new Error(`it jumped ${a.snaps + a.turnSnaps} times even without latency (${a.worstStep})`);
    return `${a.facingErr.toFixed(1)} degrees off its path (median), top turn ${a.topTurn.toFixed(0)} deg/s`;
  });
  sr.progress = 0.6;

  // 2. 150 ms of latency (and up to 30 ms of jitter) on every pose, from the moment it starts.
  S().frames = [];
  S().lag = 150; S().jitter = 30;
  srEvent('lag', '150 ms + 0-30 ms jitter on the poses');
  await sleep(10000);
  const b = framesOf(S().frames, id);
  Object.assign(sr.metrics, { framesLag: b.frames, stillFramesLag: b.still, snapsLag: b.snaps + b.turnSnaps, delayLagMs: Math.round(b.delay), extrapolatedFrames: b.aheadFrames, maxAheadMs: Math.round(b.maxAhead), topSpeedLag: Math.round(b.topSpeed * 10) / 10 });
  await srCheck('No visible snapping with 150 ms of latency', async () => {
    if (b.frames < 200) throw new Error(`only ${b.frames} frames were drawn in 10 s`);
    if (b.delay < 150) throw new Error(`the latency did not take (drawn only ${Math.round(b.delay)} ms behind)`);
    if (b.snaps + b.turnSnaps > 0) throw new Error(`${b.snaps} jumps and ${b.turnSnaps} sudden turns in ${b.frames} frames (worst ${b.worstStep})`);
    if (b.still > 0) throw new Error(`it stood still in ${b.still} of ${b.frames} frames`);
    return `${b.frames} frames, top ${b.topSpeed.toFixed(1)} m/s (walks at up to ${eightLimits.speed.toFixed(1)}), ${Math.round(b.delay)} ms behind, ${b.aheadFrames} frames predicted (up to ${Math.round(b.maxAhead)} ms)`;
  });
  sr.progress = 0.8;

  // 3. The HUD: plain latency, the detail line only after F3.
  await srCheck('The HUD shows plain latency and hides the debug line until F3', async () => {
    await waitFor(() => S().hud, 5000, 'the game to report its HUD (window.__hawkesNet.hud)');
    const h0 = S().hud;
    if (!/^Latency: \d+ ms$/.test(h0.main)) throw new Error(`the HUD says "${h0.main}", not "Latency: N ms"`);
    if (h0.debug) throw new Error(`the debug line shows before F3: "${h0.debug}"`);
    srEvent('hud', h0.main);
    await tap('F3');
    await waitFor(() => S().hud.debug, 3000, 'the debug line after F3');
    srEvent('hud', `F3 on: ${S().hud.debug}`);
    const shown = S().hud.debug, m = /predicting (\d+) ms/.exec(shown);
    if (m && Number(m[1]) > 250) throw new Error(`the HUD predicts ${m[1]} ms, more than 250`);
    await tap('F3');
    await waitFor(() => !S().hud.debug, 3000, 'the debug line to hide on the second F3');
    return `"${h0.main}"; F3 shows "${shown}", F3 again hides it`;
  });

  // 4. The other player stops sending: the avatar is carried on at most 250 ms past its last pose, then holds still.
  S().lag = 0; S().jitter = 0;
  await sleep(1500);
  clearInterval(smoothTick); smoothTick = null; // the bot's pose stream stops
  S().frames = [];
  srEvent('stop', 'the second player stops sending poses for 3 s');
  await tap('F3'); // detail on, so the footage shows "predicting N ms" capped
  await sleep(3000);
  const c = framesOf(S().frames, id);
  const tail = (S().frames || []).filter((f) => f.t > performance.now() - 1500).map((f) => (f.items || []).find((i) => i.id === id)).filter(Boolean);
  let drift = 0;
  for (let i = 1; i < tail.length; i++) drift = Math.max(drift, Math.hypot(tail[i].p[0] - tail[0].p[0], tail[i].p[2] - tail[0].p[2]));
  Object.assign(sr.metrics, { stoppedMaxAheadMs: Math.round(c.maxAhead), stoppedDriftM: Math.round(drift * 1000) / 1000 });
  await srCheck('A prediction never goes past 250 ms, and a player that stops sending holds still', async () => {
    if (c.frames < 60) throw new Error(`only ${c.frames} frames were drawn`);
    if (c.maxAhead > 250.5) throw new Error(`predicted up to ${Math.round(c.maxAhead)} ms`);
    if (Math.max(b.maxAhead, a.maxAhead) > 250.5) throw new Error(`predicted up to ${Math.round(Math.max(b.maxAhead, a.maxAhead))} ms while it walked`);
    if (tail.length < 30) throw new Error('too few frames in the last 1.5 s');
    if (drift > 0.001) throw new Error(`it drifted ${drift.toFixed(3)} m in the last 1.5 s after the poses stopped`);
    return `predicted up to ${Math.round(c.maxAhead)} ms, then held (moved ${drift.toFixed(4)} m in 1.5 s)`;
  });
  await tap('F3');
  sr.progress = 0.95;
}
// ---------------------------------------------------------------------------------------------------------------------
// "crowd": capsules never share a spot (the sample eases the local one 0.6 m away from any other, so they do not z-fight)
// and the player stays on the ground inside the edge walls (ArenaHalf 12, the capsule's centre kept within 11.5) whatever
// keys are held. Everything is read from the poses the Unity player sends, which are its own position as it plays.
const ARENA_LIMIT = 11.5, MIN_GAP = 0.6;
const flat = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);
async function crowd() {
  if (!await srCheck('Connected to Hawkes Net from the start screen', async () => {
    await joinFromStartScreen();
    if (!online()) throw new Error('the room did not come online after pressing Join');
    return 'online';
  })) return;
  sr.progress = 0.1;
  const net = slot().net;
  if (!await srCheck('A second player joined the same room', async () => {
    smoothBot = await openBotPlayer();
    await waitFor(() => (smoothBot.ready === true || smoothBot.status === 'online') && net.peers.has(smoothBot.id), 15000, 'both players to see each other');
    await waitFor(() => sent, 5000, 'the Unity player to send its place');
    return 'both in the room';
  })) return;
  // Hold the bot where it is told (a pose every 50 ms), and watch the Unity player's own poses.
  let at = [0, 1, 0];
  smoothTick = setInterval(() => { try { smoothBot.pose(at, 0); } catch { /* left */ } }, 50);
  const hold = async (keys, ms, sample) => {
    for (const k of keys) key(k, true);
    const t0 = performance.now();
    while (performance.now() - t0 < ms) { sample?.(sent); await sleep(50); }
    for (const k of keys) key(k, false);
  };
  const at1 = (p) => { at = [p[0], 1, p[2]]; };
  sr.progress = 0.2;

  // 1. Walk straight at a player standing 3 m away to the east and keep pushing for 4 s.
  const start = sent.slice();
  at1([Math.min(start[0] + 3, 8), 0, start[2]]);
  srEvent('phase', 'walk into the other player, holding D');
  let closest = 99;
  await sleep(300);
  await hold(['d'], 4000, (p) => { if (p) closest = Math.min(closest, flat(p, at)); });
  await sleep(1500);
  const rest = flat(sent, at);
  sr.metrics.closestWalkingM = r1(closest); sr.metrics.restingGapM = r1(rest);
  srEvent('gap', `closest while pushing ${closest.toFixed(2)} m, at rest ${rest.toFixed(2)} m`);
  await srCheck('Walking into another player never puts the two on one spot', async () => {
    if (closest < 0.1) throw new Error(`the two centres came ${closest.toFixed(2)} m apart: the capsules would share a spot`);
    return `closest ${closest.toFixed(2)} m`;
  });
  await srCheck(`Let go, the two stand at least ${MIN_GAP - 0.05} m apart`, async () => {
    if (rest < MIN_GAP - 0.05) throw new Error(`they stand ${rest.toFixed(2)} m apart`);
    return `${rest.toFixed(2)} m apart`;
  });
  sr.progress = 0.4;

  // 2. The other player appears exactly on this one (setup shortcut: a pose on the same spot): this one eases away.
  at1(sent);
  srEvent('phase', 'the other player lands exactly on the Unity player');
  await sleep(2500);
  const apart = flat(sent, at);
  sr.metrics.separatedGapM = r1(apart);
  await srCheck('Two avatars put on the same spot separate into two capsules', async () => {
    if (apart < MIN_GAP - 0.05) throw new Error(`still ${apart.toFixed(2)} m apart after 2.5 s`);
    return `${apart.toFixed(2)} m apart after 2.5 s`;
  });
  sr.progress = 0.5;

  // 3. Far away from the bot: 30 s of walking in every direction. Each pose must stay on the ground inside the walls.
  at = [start[0] > 0 ? -8 : 8, 1, 8]; // inside the walls and in frame: a capsule out beyond them reads as a stray object
  srEvent('phase', 'walk in every direction for 30 s');
  let worstY = 0, far = 0, bad = '', moved = 0, prev = sent.slice();
  const dirs = [['d'], ['s'], ['a'], ['w'], ['d', 'w'], ['a', 's'], ['d', 's'], ['a', 'w'], ['d'], ['w']];
  for (const keys of dirs) {
    await hold(keys, 3000, (p) => {
      if (!p) return;
      worstY = Math.max(worstY, Math.abs(p[1] - 1));
      far = Math.max(far, Math.abs(p[0]), Math.abs(p[2]));
      if (!bad && (Math.abs(p[0]) > ARENA_LIMIT + 0.02 || Math.abs(p[2]) > ARENA_LIMIT + 0.02)) bad = `at ${p.map((n) => n.toFixed(2)).join(', ')} holding ${keys.join('+')}`;
      moved += flat(p, prev); prev = p.slice();
    });
    sr.progress = Math.min(0.95, sr.progress + 0.04);
  }
  Object.assign(sr.metrics, { farthestM: r1(far), walkedM: Math.round(moved), worstHeightOffM: r1(worstY) });
  await srCheck('Walking in any direction for 30 s keeps the avatar on the ground', async () => {
    if (moved < 40) throw new Error(`the player only moved ${moved.toFixed(0)} m in 30 s of walking`);
    if (worstY > 0.01) throw new Error(`it left the ground by ${worstY.toFixed(2)} m`);
    return `walked ${moved.toFixed(0)} m, height always 1 m`;
  });
  await srCheck(`It stays inside the edge walls (within ${ARENA_LIMIT} m of the middle) and reaches them`, async () => {
    if (bad) throw new Error(`it went outside the arena ${bad}`);
    if (far < ARENA_LIMIT - 0.3) throw new Error(`it never reached an edge (farthest ${far.toFixed(1)} m)`);
    return `farthest ${far.toFixed(2)} m from the middle`;
  });
}

const smoothEnd = async () => {
  const S = globalThis.__hawkesNet;
  if (S) { S.watch = false; S.lag = 0; S.jitter = 0; }
  if (smoothTick) { clearInterval(smoothTick); smoothTick = null; }
  if (smoothBot) { const b = smoothBot; smoothBot = null; try { await b.leave(); } catch { /* gone */ } }
};

// "page": the WebGL template's early hook, demo-root.json marker, and plain error card (no Join / no second player).
async function page() {
  sr.state.scene = 'Page';
  srEvent('scene', 'Page');
  await srCheck('window.hawkesPlaytest was a stub within 1 s of page start (before Unity finished)', async () => {
    const at = globalThis.__hawkesPlaytestStubAt;
    if (typeof at !== 'number') throw new Error('the template never set window.__hawkesPlaytestStubAt');
    if (at > 1000) throw new Error(`the stub appeared at ${Math.round(at)} ms (need within 1000 ms)`);
    const stub = globalThis.__hawkesPlaytestStub;
    if (!stub || !stub._stub) throw new Error('window.__hawkesPlaytestStub is missing');
    sr.metrics.stubAtMs = Math.round(at);
    srEvent('hook-early', `${Math.round(at)} ms`);
    return `stub at ${Math.round(at)} ms`;
  });
  await srCheck('The branded loading screen filled its bar with real loader progress and faded out', async () => {
    const ls = globalThis.__hawkesLoading;
    const bar = document.getElementById('unity-loading-bar');
    if (!ls || !bar) throw new Error('the template has no loading screen');
    if (!/Hawkes\s*Net/.test(bar.textContent) || !/second tab/.test(bar.textContent)) throw new Error('loading screen lacks the title or the tip');
    if (ls.samples.length < 2 || ls.samples[ls.samples.length - 1] !== 1) throw new Error(`progress samples ${JSON.stringify(ls.samples)}`);
    if (ls.samples.some((v, i) => i && v < ls.samples[i - 1])) throw new Error('progress went backwards');
    if (ls.fadedAt === null) throw new Error('the loading screen never faded');
    srEvent('loading', `${ls.samples.length} progress steps, faded`);
    return `${ls.samples.length} progress steps, faded into the Join screen`;
  });
  await srCheck('Loading screen fits the viewport (no scroll, nothing cut off)', async () => {
    const de = document.documentElement;
    if (de.scrollWidth > innerWidth + 1 || de.scrollHeight > innerHeight + 1) throw new Error(`page ${de.scrollWidth}x${de.scrollHeight} exceeds ${innerWidth}x${innerHeight}`);
    const bar = document.getElementById('unity-loading-bar');
    const prev = [bar.hidden, bar.className];
    bar.hidden = false; bar.className = '';
    const bad = [...bar.children].filter((c) => { const r = c.getBoundingClientRect(); return r.left < 0 || r.right > innerWidth || r.top < 0 || r.bottom > innerHeight; });
    bar.hidden = prev[0]; bar.className = prev[1];
    if (bad.length) throw new Error(`${bad.length} loading element(s) outside ${innerWidth}x${innerHeight}`);
    return `fits ${innerWidth}x${innerHeight}`;
  });
  sr.progress = 0.35;
  await srCheck('demo-root.json answers next to index.html (deeper paths can relocate here)', async () => {
    const res = await fetch('./demo-root.json', { cache: 'no-store' });
    if (!res.ok) throw new Error(`./demo-root.json returned ${res.status}`);
    const body = await res.json().catch(() => ({}));
    if (body?.demo !== 'hawkes-net-unity') throw new Error(`demo-root.json was ${JSON.stringify(body)}`);
    const helper = globalThis.__hawkesDemoRoot;
    if (!helper?.find) throw new Error('window.__hawkesDemoRoot.find is missing');
    const found = await helper.find();
    // At the real demo root the first probe (./) wins and find() returns "".
    if (found !== '') throw new Error(`findDemoRoot returned ${JSON.stringify(found)}, expected "" at the demo root`);
    srEvent('demo-root', 'hawkes-net-unity');
    return 'demo-root.json ok; ./ is the demo root';
  });
  sr.progress = 0.7;
  await srCheck('A load failure shows the plain error card instead of a blank page', async () => {
    const helper = globalThis.__hawkesDemoRoot;
    const card = document.getElementById('hawkes-error-card');
    if (!helper?.showError || !card) throw new Error('the template error card is missing');
    helper.showError('playtest: simulated load failure');
    await waitFor(() => card.classList.contains('show'), 2000, 'the error card to show');
    const detail = document.getElementById('hawkes-error-detail')?.textContent || '';
    if (!/simulated load failure/.test(detail)) throw new Error(`error detail was "${detail}"`);
    srEvent('error-card', detail);
    // Put the demo back so the footage ends on the game, not the card.
    card.classList.remove('show');
    const container = document.getElementById('unity-container');
    if (container) container.style.display = '';
    const loading = document.getElementById('unity-loading-bar');
    if (loading) loading.hidden = true;
    return 'error card shown, then cleared';
  });
  sr.progress = 1;
}

api.start = async (id, options = {}) => {
  if (!SIDE.includes(id)) {
    try { await joinFromStartScreen(); } catch (e) { startLog.push({ at: performance.now(), text: `bot: ${e?.message ?? e}` }); globalThis.__hawkesJoinProblem = e?.message ?? String(e); }
    return baseStart(id, options);
  }
  if (sr && !sr.done) sr.done = true;
  await smoothEnd();
  sr = { id, t0: performance.now(), done: false, lines: [], events: [], problems: [], metrics: {}, progress: 0, state: { ready: true, status: 'running', progress: 0, scene: 'Demo' } };
  const mine = sr;
  srEvent('start', id); srEvent('scene', 'Demo'); srPanel();
  try {
    await (id === 'smooth' ? smooth() : id === 'crowd' ? crowd() : id === 'phone' ? phone() : id === 'page' ? page() : startScreen());
  } catch (e) { sr.problems.push({ severity: 'bug', message: `bot error: ${e?.message ?? e}` }); }
  finally { if (id === 'smooth' || id === 'crowd' || id === 'phone') await smoothEnd(); }
  if (mine !== sr) return;
  mine.done = true; mine.seconds = srNow();
  mine.state.status = mine.lines.length && mine.lines.every((l) => l.ok) ? 'won' : 'lost';
  mine.state.note = mine.state.status === 'won' ? 'all checks passed' : `${mine.lines.filter((l) => l.ok === false).length} check(s) failed`;
  srEvent('finish', mine.state.status); srPanel();
};
const baseState = api.state.bind(api), baseReport = api.report.bind(api), baseStop = api.stop.bind(api);
api.state = () => (sr && !sr.done ? { ...sr.state, progress: sr.progress, metrics: { ...sr.metrics } }
  : sr && SIDE.includes(api.__last) ? { ...sr.state, progress: sr.progress, ready: true, metrics: { ...sr.metrics } } : baseState());
api.report = () => (sr && SIDE.includes(api.__last) ? { scenario: sr.id, outcome: sr.done ? sr.state.status : 'stuck', seconds: r1(sr.seconds ?? srNow()), metrics: { ...sr.metrics },
  events: sr.events.slice(), problems: sr.problems.slice(), log: [...sr.lines.map((l) => `${l.ok ? 'PASS' : l.ok === false ? 'FAIL' : '...'} ${l.text}${l.why ? ` (${l.why})` : ''}`), ...startLog.map((l) => `console ${l.text}`)] } : baseReport());
api.stop = () => { if (sr && !sr.done) { sr.done = true; sr.state.status = 'done'; } smoothEnd(); baseStop(); };
// Which scenario was started last decides what state() and report() answer.
const startAny = api.start;
api.start = (id, options) => { api.__last = id; return startAny(id, options); };

// Keep the template's stub as window.hawkesPlaytest until the sample is ready, then upgrade to this api
// (and run any start() the stub queued while status was "loading").
(function promotePlaytestStub() {
  const stub = globalThis.__hawkesPlaytestStub;
  if (!stub || !stub._stub) return;
  globalThis.hawkesPlaytest = stub;
  const tick = () => {
    let ready = false;
    try { ready = Boolean(api.state()?.ready); } catch { /* not yet */ }
    if (!ready) { setTimeout(tick, 100); return; }
    globalThis.hawkesPlaytest = api;
    const q = typeof stub.takeQueue === 'function' ? stub.takeQueue() : null;
    if (q?.id) api.start(q.id, q.options || {});
  };
  tick();
})();
