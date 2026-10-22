// Prints loudness (RMS dBFS) and "harshness" (share of energy above ~4 kHz) per half second.
import { readFileSync } from "node:fs";
const buf = readFileSync(process.argv[2]), SR = 44100, data = buf.subarray(44);
const n = data.length / 4, win = SR / 2;
const hpCoef = Math.exp(-2 * Math.PI * 4000 / SR);
let lp = 0, rows = [], peak = 0;
for (let w = 0; w * win < n; w++) {
  let e = 0, h = 0;
  for (let i = w * win; i < Math.min(n, (w + 1) * win); i++) {
    const x = (data.readInt16LE(i * 4) + data.readInt16LE(i * 4 + 2)) / 65536;
    peak = Math.max(peak, Math.abs(x));
    lp = hpCoef * lp + (1 - hpCoef) * x;
    const hi = x - lp;
    e += x * x; h += hi * hi;
  }
  rows.push({ t: (w / 2).toFixed(1), rms: (10 * Math.log10(e / win + 1e-12)).toFixed(1), hf: (100 * h / (e + 1e-12)).toFixed(1) });
}
console.log("peak dBFS", (20 * Math.log10(peak)).toFixed(1));
console.log(rows.map((r) => `${r.t.padStart(4)}s ${r.rms.padStart(6)}dB hf ${r.hf.padStart(5)}%`).join("\n"));
