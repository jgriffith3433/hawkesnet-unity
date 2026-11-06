// Hawkes Studio's playtest bot on the WebGL demo's page (HawkesNetDemoBuild copies DevTools/demo-web beside index.html and
// adds this script). The Unity player's room lives in the page (HawkesNet.jslib keeps it in window.__hawkesNet), so the
// bot (net-playtest.mjs, the same file as hawkesnet-js and hawkesnet-babylon) opens a second player in that room, walks
// the Unity capsule with real key presses, and checks that the poses and messages go both ways through the bridge.
import { installNetPlaytest } from './net-playtest.mjs';

const LIVE = 'https://net.hawkesgen.ai';
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
const CODES = { d: ['KeyD', 68], w: ['KeyW', 87], 1: ['Digit1', 49], 2: ['Digit2', 50], 3: ['Digit3', 51] };
const key = (k, down) => {
  const [code, keyCode] = CODES[k];
  const init = { key: k, code, keyCode, which: keyCode, bubbles: true, cancelable: true };
  for (const t of [document.querySelector('canvas'), document, window]) t?.dispatchEvent(new KeyboardEvent(down ? 'keydown' : 'keyup', init));
};

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

installNetPlaytest({
  product: 'hawkes-net-unity',
  where: endpoint().replace(/^https?:\/\//, ''),
  ready: () => Boolean(globalThis.__hawkesNet),
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
