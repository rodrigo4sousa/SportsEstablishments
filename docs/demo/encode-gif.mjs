// Turns out/demo.webm (from record-demo.mjs) into ../images/demo.gif with ffmpeg: 960 px wide, 12 fps, own palette, endless loop.
// Usage: node encode-gif.mjs [width] [fps]   (defaults 960 and 12; lower them if the file is over 8 MB)

import { execFileSync } from "node:child_process";
import { readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const video = path.join(here, "out", "demo.webm");
const palette = path.join(here, "out", "palette.png");
const gif = path.join(here, "..", "images", "demo.gif");
const width = Number(process.argv[2] ?? 960);
const fps = Number(process.argv[3] ?? 12);
const { trimStart } = JSON.parse(await readFile(path.join(here, "out", "timing.json"), "utf8"));

const scale = `fps=${fps},scale=${width}:-1:flags=lanczos`;
const ffmpeg = (args) => execFileSync("ffmpeg", ["-v", "error", "-y", ...args], { stdio: "inherit" });

ffmpeg(["-ss", String(trimStart), "-i", video, "-vf", `${scale},palettegen=stats_mode=diff`, palette]);
ffmpeg([
  "-ss", String(trimStart), "-i", video, "-i", palette,
  "-lavfi", `${scale}[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle`,
  "-loop", "0", gif,
]);

const { size } = await stat(gif);
console.log(`Wrote docs/images/demo.gif: ${(size / 1024 / 1024).toFixed(2)} MB${size > 8 * 1024 * 1024 ? " (over 8 MB: lower the width or fps)" : ""}.`);
