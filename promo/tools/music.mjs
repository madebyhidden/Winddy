// Original background track for the Winddy reel: soft, warm electronic pop at 112 BPM.
// Everything is synthesized here (no samples), using mostly sine-based voices so nothing
// sounds harsh on laptop speakers:  node music.mjs out.wav [seconds]
import { writeFileSync } from "node:fs";

const SR = 44100;
const BPM = 112;
const beat = 60 / BPM;
const bar = beat * 4;
const seconds = Number(process.argv[3] ?? 30);
const N = Math.ceil(seconds * SR);
const L = new Float32Array(N), R = new Float32Array(N);

const midi = (n) => 440 * 2 ** ((n - 69) / 12);
const rand = (() => { let s = 1234567; return () => ((s = (s * 1103515245 + 12345) >>> 0) / 4294967296) * 2 - 1; })();

function add(start, buf, gain = 1, pan = 0) {
  const i0 = Math.round(start * SR);
  const gl = gain * Math.cos((pan + 1) * Math.PI / 4), gr = gain * Math.sin((pan + 1) * Math.PI / 4);
  for (let i = 0; i < buf.length && i0 + i < N; i++) {
    if (i0 + i < 0) continue;
    L[i0 + i] += buf[i] * gl;
    R[i0 + i] += buf[i] * gr;
  }
}

// Short fade at both ends of every sound: no clicks.
function declick(out, fadeIn = 0.004, fadeOut = 0.03) {
  const a = Math.round(fadeIn * SR), b = Math.round(fadeOut * SR);
  for (let i = 0; i < a && i < out.length; i++) out[i] *= i / a;
  for (let i = 0; i < b && i < out.length; i++) out[out.length - 1 - i] *= i / b;
  return out;
}

// ── instruments ──
// Round, low kick: pitched-down sine only.
function kick() {
  const n = Math.round(0.4 * SR), out = new Float32Array(n);
  let ph = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR, f = 62 + 60 * Math.exp(-t * 28); // above the range laptop speakers rattle at
    ph += 2 * Math.PI * f / SR;
    out[i] = Math.sin(ph) * Math.exp(-t * 8);
  }
  return declick(out, 0.002, 0.05);
}

// Soft rim/snare: a short tonal body with only a whisper of darkened noise.
function snare() {
  const n = Math.round(0.22 * SR), out = new Float32Array(n);
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    lp += 0.18 * (rand() - lp); // dark noise
    out[i] = Math.sin(2 * Math.PI * 190 * t) * Math.exp(-t * 30) * 0.7 + lp * Math.exp(-t * 26) * 0.9;
  }
  return declick(out, 0.002, 0.04);
}

// Gentle shaker: band-limited noise, very quiet.
function shaker() {
  const n = Math.round(0.07 * SR), out = new Float32Array(n);
  let lp = 0, prev = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    lp += 0.35 * (rand() - lp);
    const hp = lp - prev; prev = lp;
    out[i] = hp * Math.sin(Math.PI * t / 0.07) * 0.6;
  }
  return out;
}

// Warm pad: stacked sines with slow vibrato and a soft swell.
function pad(notes, dur) {
  const n = Math.round(dur * SR), out = new Float32Array(n);
  const voices = notes.flatMap((m) => [-0.06, 0.06].map((d) => ({ f: midi(m + d), ph: rand() * Math.PI })));
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    let s = 0;
    for (const v of voices) s += Math.sin(2 * Math.PI * v.f * t + v.ph) + 0.18 * Math.sin(4 * Math.PI * v.f * t + v.ph);
    s /= voices.length;
    const env = Math.min(1, t / 0.5) * Math.min(1, (dur - t) / 0.5);
    out[i] = s * env * (0.85 + 0.15 * Math.sin(t * 2.2));
  }
  return out;
}

// Electric-piano-like note: sine plus a little 2nd harmonic, mellow decay.
function keys(m, dur = 0.6) {
  const n = Math.round(dur * SR), out = new Float32Array(n), f = midi(m);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    out[i] = (Math.sin(2 * Math.PI * f * t) + 0.25 * Math.sin(4 * Math.PI * f * t) * Math.exp(-t * 8)) * Math.exp(-t * 4.5);
  }
  return declick(out, 0.006, 0.05);
}

// Sub bass: pure sine with soft envelope.
function bass(m, dur) {
  const n = Math.round(dur * SR), out = new Float32Array(n), f = midi(m);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    out[i] = (Math.sin(2 * Math.PI * f * t) + 0.12 * Math.sin(4 * Math.PI * f * t)) * Math.exp(-t * 0.9);
  }
  return declick(out, 0.01, 0.06);
}

// ── arrangement ──
// Fmaj7 – G6 – Em7 – Am(add9)
const chords = [
  { pad: [53, 57, 60, 64], bass: 41, arp: [65, 69, 72, 76] },
  { pad: [55, 59, 62, 64], bass: 43, arp: [67, 71, 74, 76] },
  { pad: [52, 55, 59, 62], bass: 40, arp: [64, 67, 71, 74] },
  { pad: [57, 60, 64, 71], bass: 45, arp: [69, 72, 76, 79] },
];
const K = kick(), S = snare(), SH = shaker();
const bars = Math.ceil(seconds / bar);
const dropBar = 2;                         // beat enters here (~4.3 s), matching the video
const lastBeatBar = bars - 2;              // drums leave for a calm ending

for (let b = 0; b < bars; b++) {
  const ch = chords[b % 4], t0 = b * bar;
  const ending = b > lastBeatBar;
  add(t0, pad(ending ? chords[3].pad : ch.pad, bar + 0.5), b < dropBar ? 0.2 : 0.16);

  // arpeggio on electric piano
  const order = [0, 1, 2, 3, 2, 1, 2, 3];
  for (let s = 0; s < 8; s++) {
    if ((b < 1 || ending) && s % 2) continue;
    add(t0 + s * beat / 2, keys(ch.arp[order[s]], 0.55), b < dropBar ? 0.13 : 0.11, s % 2 ? 0.3 : -0.3);
  }

  if (b >= dropBar && b <= lastBeatBar) {
    // first bar of the beat comes in a little quieter so the entrance is smooth
    const enter = b === dropBar ? 0.75 : 1;
    for (let q = 0; q < 4; q++) {
      add(t0 + q * beat, K, 0.5 * enter);
      add(t0 + q * beat + beat / 2, SH, 0.09 * enter, 0.25);
      add(t0 + q * beat + beat / 4 * 3, SH, 0.05 * enter, -0.25);
    }
    add(t0 + beat, S, 0.22 * enter);
    add(t0 + beat * 3, S, 0.22 * enter);
    // bass an octave up (110–220 Hz): audible on small speakers without making them buzz
    add(t0, bass(ch.bass + 12, beat * 1.6), 0.24 * enter);
    add(t0 + beat * 2, bass(ch.bass + 12, beat * 1.4), 0.2 * enter);
  }
  // soft pickup into the beat instead of a noise sweep
  if (b === dropBar - 1) {
    add(t0 + beat * 3, keys(72, 0.4), 0.09, -0.2);
    add(t0 + beat * 3.5, keys(74, 0.4), 0.09, 0.2);
  }
}
// closing chord: low and round, fading out with the picture
const endT = seconds - bar * 1.2;
for (const [m, p] of [[57, -0.3], [64, 0.3], [69, 0], [72, 0.15]]) add(endT, keys(m, 2.6), 0.08, p);

// ── master: gentle stereo echo, low-pass to tame fizz, smooth limiter, fades ──
const d = Math.round(beat * 0.75 * SR);
for (let i = N - 1; i >= d; i--) { L[i] += R[i - d] * 0.12; R[i] += L[i - d] * 0.12; }
const lpCoef = 1 - Math.exp(-2 * Math.PI * 7000 / SR);
let lpl = 0, lpr = 0, gainEnv = 1;
const pcm = Buffer.alloc(N * 4);
for (let i = 0; i < N; i++) {
  const t = i / SR;
  lpl += lpCoef * (L[i] - lpl);
  lpr += lpCoef * (R[i] - lpr);
  // peak limiter at -3 dBFS with slow release (no waveshaping, so no distortion)
  const peak = Math.max(Math.abs(lpl), Math.abs(lpr)) * gainEnv;
  if (peak > 0.7) gainEnv *= 0.7 / peak;
  else gainEnv += (1 - gainEnv) * 0.00005;
  const fade = Math.min(1, t / 0.5) * Math.min(1, (seconds - t) / 2.5);
  pcm.writeInt16LE(Math.round(lpl * gainEnv * fade * 32767), i * 4);
  pcm.writeInt16LE(Math.round(lpr * gainEnv * fade * 32767), i * 4 + 2);
}
const header = Buffer.alloc(44);
header.write("RIFF", 0); header.writeUInt32LE(36 + pcm.length, 4); header.write("WAVE", 8);
header.write("fmt ", 12); header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(2, 22);
header.writeUInt32LE(SR, 24); header.writeUInt32LE(SR * 4, 28); header.writeUInt16LE(4, 32); header.writeUInt16LE(16, 34);
header.write("data", 36); header.writeUInt32LE(pcm.length, 40);
writeFileSync(process.argv[2], Buffer.concat([header, pcm]));
console.log(`wrote ${process.argv[2]}: ${seconds}s, ${BPM} BPM, bar = ${bar.toFixed(3)}s`);
