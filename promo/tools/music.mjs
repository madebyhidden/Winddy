// Original background track for the Winddy reel: light, airy electronic pop at 112 BPM.
// Everything is synthesized here (no samples):  node music.mjs out.wav [seconds]
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

// ── instruments ──
function kick() {
  const n = Math.round(0.35 * SR), out = new Float32Array(n);
  let ph = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR, f = 48 + 110 * Math.exp(-t * 30);
    ph += 2 * Math.PI * f / SR;
    out[i] = Math.sin(ph) * Math.exp(-t * 9) + (i < 60 ? rand() * 0.3 * (1 - i / 60) : 0);
  }
  return out;
}

function clap() {
  const n = Math.round(0.25 * SR), out = new Float32Array(n);
  let lp = 0, prev = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const env = (t < 0.03 ? 0.6 + 0.4 * Math.abs(Math.sin(t * 330)) : 1) * Math.exp(-t * 22);
    const x = rand();
    lp += 0.45 * (x - lp);
    const hp = lp - prev; prev = lp; // rough band-pass
    out[i] = hp * env * 2.2;
  }
  return out;
}

function hat(open = false) {
  const n = Math.round((open ? 0.22 : 0.06) * SR), out = new Float32Array(n);
  let prev = 0;
  for (let i = 0; i < n; i++) {
    const x = rand(), hp = x - prev; prev = x;
    out[i] = hp * Math.exp(-(i / SR) * (open ? 14 : 70)) * 0.5;
  }
  return out;
}

// Soft detuned-saw pad through a one-pole low-pass, slow attack.
function pad(notes, dur) {
  const n = Math.round(dur * SR), out = new Float32Array(n);
  const voices = notes.flatMap((m) => [-0.08, 0.0, 0.07].map((d) => ({ f: midi(m + d), ph: Math.random() })));
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    let s = 0;
    for (const v of voices) { v.ph = (v.ph + v.f / SR) % 1; s += v.ph * 2 - 1; }
    s /= voices.length;
    const cutoff = 0.05 + 0.03 * Math.sin(t * 1.3);
    lp += cutoff * (s - lp);
    const env = Math.min(1, t / 0.6) * Math.min(1, (dur - t) / 0.5);
    out[i] = lp * env;
  }
  return out;
}

// Plucked bell-ish note (two partials, fast decay).
function pluck(m, dur = 0.5) {
  const n = Math.round(dur * SR), out = new Float32Array(n), f = midi(m);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    out[i] = (Math.sin(2 * Math.PI * f * t) + 0.35 * Math.sin(2 * Math.PI * f * 2.01 * t) * Math.exp(-t * 12)) * Math.exp(-t * 6) * Math.min(1, t * 400);
  }
  return out;
}

function bass(m, dur) {
  const n = Math.round(dur * SR), out = new Float32Array(n), f = midi(m);
  let lp = 0, ph = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    ph = (ph + f / SR) % 1;
    const s = Math.sin(2 * Math.PI * ph) + 0.3 * (ph * 2 - 1);
    lp += 0.12 * (s - lp);
    out[i] = lp * Math.min(1, t * 200) * Math.min(1, (dur - t) * 30) * Math.exp(-t * 1.2);
  }
  return out;
}

// Rising noise swell used before the drop and the final card.
function riser(dur) {
  const n = Math.round(dur * SR), out = new Float32Array(n);
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR, k = t / dur;
    lp += (0.02 + 0.4 * k * k) * (rand() - lp);
    out[i] = lp * k * k * 0.9;
  }
  return out;
}

// ── arrangement ──
// Fmaj7 – G6 – Em7 – Am(add9), voiced around middle C.
const chords = [
  { pad: [53, 57, 60, 64], bass: 41, arp: [65, 69, 72, 76] },
  { pad: [55, 59, 62, 64], bass: 43, arp: [67, 71, 74, 76] },
  { pad: [52, 55, 59, 62], bass: 40, arp: [64, 67, 71, 74] },
  { pad: [57, 60, 64, 71], bass: 45, arp: [69, 72, 76, 79] },
];
const K = kick(), C = clap(), H = hat(), HO = hat(true);
const bars = Math.ceil(seconds / bar);
const dropBar = 2; // drums enter after the intro (~4.3 s)

for (let b = 0; b < bars; b++) {
  const ch = chords[b % 4], t0 = b * bar;
  const outro = t0 > seconds - bar * 1.5;
  add(t0, pad(ch.pad, bar + 0.4), 0.22, 0);

  // arpeggio: 8ths, pattern up/down, panned slightly
  const order = [0, 1, 2, 3, 2, 1, 2, 3];
  for (let s = 0; s < 8; s++) {
    if (b < 1 && s % 2) continue; // sparser first bar
    add(t0 + s * beat / 2, pluck(ch.arp[order[s]], 0.45), 0.13, s % 2 ? 0.35 : -0.35);
  }

  if (b >= dropBar && !outro) {
    for (let q = 0; q < 4; q++) {
      add(t0 + q * beat, K, 0.75);
      add(t0 + q * beat + beat / 2, q === 3 ? HO : H, 0.22, 0.2);
      add(t0 + q * beat, H, 0.12, -0.2);
    }
    add(t0 + beat, C, 0.32);
    add(t0 + beat * 3, C, 0.32);
    add(t0, bass(ch.bass, beat * 1.5), 0.42);
    add(t0 + beat * 2, bass(ch.bass, beat * 0.9), 0.32);
    add(t0 + beat * 3, bass(ch.bass + 12, beat * 0.9), 0.22);
  }
  if (b === dropBar - 1) add(t0, riser(bar), 0.3);
}
// swell into the final call-to-action and a closing chord
add(seconds - bar * 2.5, riser(bar), 0.25);
add(seconds - bar * 1.5, pluck(84, 1.6), 0.12, -0.2);
add(seconds - bar * 1.5 + 0.12, pluck(88, 1.6), 0.1, 0.2);

// ── master: gentle stereo delay, soft clip, fades ──
const d = Math.round(beat * 0.75 * SR);
for (let i = N - 1; i >= d; i--) { L[i] += R[i - d] * 0.18; R[i] += L[i - d] * 0.18; }
const pcm = Buffer.alloc(N * 4);
for (let i = 0; i < N; i++) {
  const t = i / SR;
  const fade = Math.min(1, t / 0.4) * Math.min(1, (seconds - t) / 2.0);
  const l = Math.tanh(L[i] * 1.1) * fade * 0.9, r = Math.tanh(R[i] * 1.1) * fade * 0.9;
  pcm.writeInt16LE(Math.round(l * 32767), i * 4);
  pcm.writeInt16LE(Math.round(r * 32767), i * 4 + 2);
}
const header = Buffer.alloc(44);
header.write("RIFF", 0); header.writeUInt32LE(36 + pcm.length, 4); header.write("WAVE", 8);
header.write("fmt ", 12); header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(2, 22);
header.writeUInt32LE(SR, 24); header.writeUInt32LE(SR * 4, 28); header.writeUInt16LE(4, 32); header.writeUInt16LE(16, 34);
header.write("data", 36); header.writeUInt32LE(pcm.length, 40);
writeFileSync(process.argv[2], Buffer.concat([header, pcm]));
console.log(`wrote ${process.argv[2]}: ${seconds}s, ${BPM} BPM, bar = ${bar.toFixed(3)}s`);
