// Synthesized tile-clack — no audio assets needed.
let _ctx;
window.playClack = function () {
  try {
    _ctx ||= new (window.AudioContext || window.webkitAudioContext)();
    const t = _ctx.currentTime;
    // short noise burst = tile hitting the table
    const n = _ctx.createBufferSource();
    const buf = _ctx.createBuffer(1, _ctx.sampleRate * 0.05, _ctx.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = (Math.random() * 2 - 1) * Math.exp(-i / (d.length * 0.2));
    n.buffer = buf;
    const f = _ctx.createBiquadFilter();
    f.type = 'highpass'; f.frequency.value = 2200;
    const g = _ctx.createGain();
    g.gain.setValueAtTime(0.25, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.08);
    n.connect(f).connect(g).connect(_ctx.destination);
    n.start(t); n.stop(t + 0.08);
  } catch { /* audio blocked — ignore */ }
};
