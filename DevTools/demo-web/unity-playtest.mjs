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
const main = () => {
  const s = slot();
  const net = s?.net ?? null;
  // What the Unity player sends (HawkesNet_Pose calls net.pose): its own place, as the other player should see it.
  if (net && !net.__hnptPose) {
    const pose = net.pose.bind(net);
    net.pose = (p, y, e) => { sent = Array.from(p).slice(0, 3); return pose(p, y, e); };
    net.__hnptPose = true;
  }
  return net;
};
// Unity reads the keyboard from the page: a press on the canvas and the window, with the key's code as a browser sends it.
const CODES = { d: ['KeyD', 68], w: ['KeyW', 87] };
const key = (k, down) => {
  const [code, keyCode] = CODES[k];
  const init = { key: k, code, keyCode, which: keyCode, bubbles: true, cancelable: true };
  for (const t of [document.querySelector('canvas'), document, window]) t?.dispatchEvent(new KeyboardEvent(down ? 'keydown' : 'keyup', init));
};

installNetPlaytest({
  product: 'hawkes-net-unity',
  where: endpoint().replace(/^https?:\/\//, ''),
  ready: () => Boolean(globalThis.__hawkesNet),
  main,
  openBot: async () => {
    const { join } = await import(`${endpoint()}/sdk/hawkes-net@1.mjs`);
    const m = main();
    return join(m.room, { project: 'netdemo', endpoint: endpoint(), transport: m.transport === 'p2p' ? 'auto' : 'relay', name: 'Playtest bot', role: 'player', sub: `pt-${Math.random()}`, log: false });
  },
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
