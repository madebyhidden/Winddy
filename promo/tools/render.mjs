// Renders an HTML page with headless Chrome over the DevTools protocol.
// The page exposes `window.renderFrame(t)` (seconds); every frame is saved as PNG.
//
//   node render.mjs page.html outDir --w 1080 --h 1920 --fps 30 --dur 30
//   node render.mjs icon.html out.png --w 1024 --h 1024          (single frame, transparent)
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const args = process.argv.slice(2);
const opt = (name, def) => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 ? Number(args[i + 1]) : def;
};
const [page, out] = args;
const width = opt("w", 1080), height = opt("h", 1920), fps = opt("fps", 30), dur = opt("dur", 0);
const single = dur === 0;

const chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const port = 9300 + Math.floor(Math.random() * 500);
const proc = spawn(chrome, [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${mkdtempSync(join(tmpdir(), "render-"))}`,
  "--hide-scrollbars", "--force-device-scale-factor=1", "--allow-file-access-from-files", "--no-first-run", "about:blank",
], { stdio: "ignore" });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let targets;
for (let i = 0; i < 100; i++) {
  try { targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json(); if (targets.length) break; } catch {}
  await sleep(100);
}
const ws = new WebSocket(targets.find((t) => t.type === "page").webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener("open", r));
let id = 0;
const pending = new Map();
ws.addEventListener("message", (e) => {
  const msg = JSON.parse(e.data);
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
});
const send = (method, params = {}) => new Promise((res, rej) => {
  const mid = ++id;
  pending.set(mid, (m) => (m.error ? rej(new Error(`${method}: ${m.error.message}`)) : res(m.result)));
  ws.send(JSON.stringify({ id: mid, method, params }));
});
const evaluate = async (expression) => {
  const r = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
  return r.result.value;
};

await send("Page.enable");
await send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile: false });
await send("Emulation.setDefaultBackgroundColorOverride", { color: { r: 0, g: 0, b: 0, a: 0 } });
const [pagePath, query] = page.split("?");
await send("Page.navigate", { url: pathToFileURL(resolve(pagePath)).href + (query ? `?${query}` : "") });
for (let i = 0; i < 100 && (await evaluate("document.readyState")) !== "complete"; i++) await sleep(50);
await evaluate("document.fonts.ready.then(() => true)");
await evaluate("window.ready ? window.ready() : true");

const shot = async () => Buffer.from((await send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false })).data, "base64");

if (single) {
  await evaluate(`window.renderFrame ? window.renderFrame(${opt("t", 0)}) : true`);
  writeFileSync(out, await shot());
} else {
  mkdirSync(out, { recursive: true });
  const total = Math.round(dur * fps);
  for (let f = 0; f < total; f++) {
    await evaluate(`window.renderFrame(${f / fps})`);
    writeFileSync(join(out, `f${String(f).padStart(5, "0")}.png`), await shot());
    if (f % 30 === 0) process.stdout.write(`\r${f}/${total}`);
  }
  process.stdout.write(`\r${total}/${total}\n`);
}
ws.close();
proc.kill();
