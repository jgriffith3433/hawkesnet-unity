// The bridge from HawkesNet.cs to Hawkes Net's web SDK (hawkes-net.mjs, Hawkes Gen docs/net.md), which runs in the page.
// Rooms live in window.__hawkesNet by handle. Events go to the C# side with SendMessage(<receiver>, 'OnNet', json); avatar
// poses are kept per room and the changed ones read once a frame (HawkesNet_Poses), so many players don't mean many
// SendMessage calls. Nothing here throws into the game.
mergeInto(LibraryManager.library, {
  HawkesNet_Join: function (handle, roomPtr, optsPtr, receiverPtr) {
    var room = UTF8ToString(roomPtr), receiver = UTF8ToString(receiverPtr), o = {};
    try { o = JSON.parse(UTF8ToString(optsPtr) || '{}'); } catch (e) {}
    var S = window.__hawkesNet = window.__hawkesNet || { rooms: {} };
    var tell = function (e, x) {
      x = x || {};
      x.h = handle; x.e = e;
      try { SendMessage(receiver, 'OnNet', JSON.stringify(x)); } catch (err) {}
    };
    var who = function (p) { return p ? { peer: p.id || '', name: p.name || '', ord: p.ord || 0, away: !!p.away } : { peer: '', name: '', ord: 0, away: false }; };
    // poses: peer -> { p, y, at, e (the extra values as JSON) }; changed: the peers whose pose changed since the last read.
    var slot = S.rooms[handle] = { net: null, poses: {}, changed: {}, dirty: false, gone: false, extraKeys: [] };
    var sdk = o.sdkUrl || 'https://net.hawkesgen.com/sdk/hawkes-net@1.mjs';
    var opts = { name: o.name || undefined, transport: o.transport || 'auto', role: o.role || 'player', log: o.log !== false };
    if (o.project) opts.project = o.project;
    if (o.endpoint) opts.endpoint = o.endpoint;
    if (o.config) opts.config = o.config;
    if (o.leaveOnHide === false) opts.leaveOnHide = false;
    if (o.ticketUrl) {
      // The game's own server hands out tickets (it asks hawkesgen.com with its API key): POST { room, name } -> Gen's answer.
      opts.ticket = function () {
        return fetch(o.ticketUrl, { method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: o.ticketCredentials ? 'include' : 'same-origin', body: JSON.stringify({ room: room, name: o.name || null }) })
          .then(function (r) { if (!r.ok) throw new Error('ticket refused (' + r.status + ')'); return r.json(); });
      };
    }
    // import() through Function: some Unity versions' build step can't parse a dynamic import in a jslib.
    // (A page whose policy forbids it would throw here, outside any promise: caught, it is just a failed join.)
    var load;
    try { load = (new Function('u', 'return import(u)'))(sdk); } catch (e) { load = Promise.reject(e); }
    load.then(function (m) { return m.join(room, opts); }).then(function (net) {
      if (slot.gone) { net.leave(); return; }
      slot.net = net;
      net.on('*', function (data, from, meta) {
        var w = who(from);
        // Text sent with HawkesNet_SendText arrives as it was sent; anything else as JSON.
        var text = typeof data === 'string';
        tell('msg', { kind: meta.kind, data: text ? data : JSON.stringify(data === undefined ? null : data), text: text, peer: w.peer, name: w.name, reliable: meta.reliable });
      });
      net.onPose(function (peer, s, at) {
        var p = s && s.p;
        if (!p) return;
        var extra = null;
        for (var k in s) {
          // A key the sender stopped sending comes as null once (HawkesNet_Pose): it is gone.
          if (k === 'p' || k === 'y' || s[k] === null || s[k] === undefined) continue;
          (extra = extra || {})[k] = s[k];
        }
        slot.poses[peer] = { p: [p[0], p[1], p[2]], y: s.y || 0, at: at || 0, e: extra ? JSON.stringify(extra) : '' };
        slot.changed[peer] = true;
        slot.dirty = true;
      });
      net.when('peer:join', function (p) { tell('join', who(p)); });
      net.when('peer:leave', function (x) { delete slot.poses[x.peer.id]; delete slot.changed[x.peer.id]; tell('leave', { peer: x.peer.id, name: x.peer.name || '', reason: x.reason || '' }); });
      net.when('host', function (x) { tell('host', { peer: x.host || '', reason: x.reason || '' }); });
      // A player's page in the background (Hawkes Gen docs/net.md "In the background"), and this page's own.
      net.when('peer:away', function (x) { tell('away', { peer: x.peer.id, name: x.peer.name || '', away: !!x.away }); });
      net.when('away', function (x) { tell('selfaway', { away: !!x.away }); });
      net.when('transport', function (x) { tell('transport', { transport: x.to, reason: x.why || '' }); });
      net.when('status', function (x) { tell('status', { status: x.status, transport: x.transport }); });
      net.when('problem', function (x) { tell('problem', { reason: x.code, data: String(x.detail || '') }); });
      var peers = [];
      net.peers.forEach(function (p) { peers.push(who(p)); });
      tell('ready', { peer: net.id, name: net.name || '', transport: net.transport, host: net.host || '', ord: net.ord || 0, data: JSON.stringify(peers) });
    }).catch(function (e) {
      tell('error', { reason: String((e && e.code) || 'join'), data: String((e && e.message) || e) });
    });
  },

  // dataPtr is JSON (or empty for null). An unreliable message replaces one of its kind (to the same peer) still queued.
  HawkesNet_Send: function (handle, kindPtr, dataPtr, reliable, toPtr) {
    try {
      var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
      if (!slot || !slot.net) return 0;
      var data = null, raw = UTF8ToString(dataPtr), to = UTF8ToString(toPtr), kind = UTF8ToString(kindPtr);
      if (raw) data = JSON.parse(raw);
      var opt = { reliable: !!reliable };
      if (to) opt.to = to;
      if (!reliable) opt.key = kind + (to ? '>' + to : '');
      slot.net.send(kind, data, opt);
      return 1;
    } catch (e) { return 0; }
  },

  // Like HawkesNet_Send, with the game's own text as the data: it arrives as the same text (the msg event's text flag).
  HawkesNet_SendText: function (handle, kindPtr, textPtr, reliable, toPtr) {
    try {
      var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
      if (!slot || !slot.net) return 0;
      var to = UTF8ToString(toPtr), kind = UTF8ToString(kindPtr);
      var opt = { reliable: !!reliable };
      if (to) opt.to = to;
      if (!reliable) opt.key = kind + (to ? '>' + to : '');
      slot.net.send(kind, UTF8ToString(textPtr), opt);
      return 1;
    } catch (e) { return 0; }
  },

  // My avatar (yaw in radians) and its extra values: a JSON object, or empty. A key left out after being sent goes as
  // null once, so the others drop it (the SDK keeps a peer's pose by merging what arrives).
  HawkesNet_Pose: function (handle, x, y, z, yaw, extraPtr) {
    var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
    if (!slot || !slot.net) return;
    try {
      var raw = extraPtr ? UTF8ToString(extraPtr) : '', extra = raw ? JSON.parse(raw) : null, keys = [];
      if (extra && typeof extra === 'object') {
        delete extra.p; delete extra.y;
        for (var k in extra) keys.push(k);
      } else extra = null;
      for (var i = 0; i < slot.extraKeys.length; i++) if (!extra || !(slot.extraKeys[i] in extra)) (extra = extra || {})[slot.extraKeys[i]] = null;
      slot.extraKeys = keys;
      slot.net.pose([x, y, z], yaw, extra);
    } catch (e) {}
  },

  // The poses that changed since the last read, as {"items":[{"id","x","y","z","r","at","e"}]}, else null.
  HawkesNet_Poses: function (handle) {
    var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
    if (!slot || !slot.dirty) return null;
    slot.dirty = false;
    var out = [];
    for (var id in slot.changed) {
      var p = slot.poses[id];
      if (p) out.push({ id: id, x: p.p[0], y: p.p[1], z: p.p[2], r: p.y, at: p.at, e: p.e });
    }
    slot.changed = {};
    var s = JSON.stringify({ items: out });
    var n = lengthBytesUTF8(s) + 1, buf = _malloc(n);
    stringToUTF8(s, buf, n);
    return buf;
  },

  HawkesNet_Now: function (handle) {
    var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
    return slot && slot.net ? slot.net.now() : Date.now();
  },

  HawkesNet_Migrate: function (handle) {
    var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
    if (slot && slot.net) try { slot.net.migrate('game asked'); } catch (e) {}
  },

  HawkesNet_SetAway: function (handle, on) {
    var slot = (window.__hawkesNet || { rooms: {} }).rooms[handle];
    if (slot && slot.net) try { slot.net.setAway(!!on); } catch (e) {}
  },

  // Keeps Unity running while the page is in the background (no animation frames: a hidden tab, a minimised or covered
  // window): the SDK's background() runs one iteration of Unity's main loop about 60 times a second until frames come
  // back (Hawkes Gen docs/net.md "In the background"). Returns 1 when Unity's main loop was found.
  HawkesNet_RunInBackground__deps: ['$Browser'],
  HawkesNet_RunInBackground: function (on, sdkPtr) {
    var S = window.__hawkesNet = window.__hawkesNet || { rooms: {} };
    S.bgWanted = !!on;
    if (S.bgStop) { try { S.bgStop(); } catch (e) {} S.bgStop = null; }
    if (!on) return 1;
    // Unity's loop (Emscripten's): MainLoop in newer Emscripten, Browser.mainLoop before.
    var loop = (typeof MainLoop !== 'undefined' && MainLoop && MainLoop.func) ? MainLoop : (Browser && Browser.mainLoop);
    if (!loop || !loop.func) { console.warn('[HawkesNet] RunInBackground: Unity\'s main loop was not found; the game pauses in the background'); return 0; }
    var step = function () {
      var l = (typeof MainLoop !== 'undefined' && MainLoop && MainLoop.func) ? MainLoop : Browser.mainLoop;
      if (!l || !l.func) return;
      try { if (l.runIter) l.runIter(l.func); else l.func(); }
      catch (e) { if (e !== 'unwind' && !(e && e.name === 'ExitStatus')) console.error('[HawkesNet] background step:', e); }
    };
    var sdk = UTF8ToString(sdkPtr) || 'https://net.hawkesgen.com/sdk/hawkes-net@1.mjs';
    var load;
    try { load = (new Function('u', 'return import(u)'))(sdk); } catch (e) { load = Promise.reject(e); }
    load.then(function (m) {
      if (!S.bgWanted || S.bgStop) return;
      S.bgStop = m.background(step);
    }).catch(function (e) { console.warn('[HawkesNet] RunInBackground: the SDK did not load (' + ((e && e.message) || e) + ')'); });
    return 1;
  },

  HawkesNet_Leave: function (handle) {
    var S = window.__hawkesNet || { rooms: {} }, slot = S.rooms[handle];
    if (!slot) return;
    slot.gone = true;
    if (slot.net) try { slot.net.leave(); } catch (e) {}
    delete S.rooms[handle];
  },

  // How many players each room has (the public counts of a project with its own sites: GET /v1/net/counts). The answer
  // goes to the receiver's OnNetCount as {"id","ok","rooms":{"<room>":n},"error"}.
  HawkesNet_Count: function (id, endpointPtr, projectPtr, roomsPtr, receiverPtr) {
    var receiver = UTF8ToString(receiverPtr);
    var answer = function (x) { x.id = id; try { SendMessage(receiver, 'OnNetCount', JSON.stringify(x)); } catch (err) {} };
    try {
      var url = (UTF8ToString(endpointPtr) || 'https://net.hawkesgen.com') + '/v1/net/counts?project=' + encodeURIComponent(UTF8ToString(projectPtr)) + '&rooms=' + encodeURIComponent(UTF8ToString(roomsPtr));
      fetch(url).then(function (r) { return r.json().then(function (b) { return { ok: r.ok, b: b }; }); })
        .then(function (x) { answer(x.ok ? { ok: true, rooms: x.b.rooms || {} } : { ok: false, error: String((x.b && x.b.error) || 'refused') }); })
        .catch(function (e) { answer({ ok: false, error: String((e && e.message) || e) }); });
    } catch (e) { answer({ ok: false, error: String(e) }); }
  },
});
