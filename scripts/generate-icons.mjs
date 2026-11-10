// Makes the raster icons of the site from frontend/src/app/icon.svg, the source of truth. Uses the Chromium of the E2E tests, so it adds
// no dependency to the project. See scripts/README.md.
//
//   node scripts/generate-icons.mjs

import { readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const appDir = path.join(root, "frontend", "src", "app");
const { chromium } = createRequire(path.join(root, "e2e", "package.json"))("@playwright/test");

const ICO_SIZES = [16, 32, 48];
const APPLE_SIZE = 180;
/** The icon's background: iOS paints transparent corners black and rounds the corners itself, so the Apple icon has none. */
const BACKGROUND = "#171717";

const svg = await readFile(path.join(appDir, "icon.svg"), "utf8");
const browser = await chromium.launch();

async function renderPng(size, background) {
  const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
  const sized = svg.replace("<svg ", `<svg width="${size}" height="${size}" style="display:block" `);
  await page.setContent(`<!doctype html><body style="margin:0;background:${background}">${sized}</body>`);
  const png = await page.screenshot({ omitBackground: background === "transparent" });
  await page.close();
  return png;
}

/** An .ico holding PNG images: a 6-byte header, a 16-byte entry per image, then the images. */
function buildIco(images) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(1, 2); // type: icon
  header.writeUInt16LE(images.length, 4);

  let offset = header.length + 16 * images.length;
  const entries = images.map(({ size, png }) => {
    const entry = Buffer.alloc(16);
    entry.writeUInt8(size, 0);
    entry.writeUInt8(size, 1);
    entry.writeUInt16LE(1, 4); // colour planes
    entry.writeUInt16LE(32, 6); // bits per pixel
    entry.writeUInt32LE(png.length, 8);
    entry.writeUInt32LE(offset, 12);
    offset += png.length;
    return entry;
  });
  return Buffer.concat([header, ...entries, ...images.map(({ png }) => png)]);
}

const frames = [];
for (const size of ICO_SIZES) {
  frames.push({ size, png: await renderPng(size, "transparent") });
}
await writeFile(path.join(appDir, "favicon.ico"), buildIco(frames));
await writeFile(path.join(appDir, "apple-icon.png"), await renderPng(APPLE_SIZE, BACKGROUND));
await browser.close();
console.log(`Wrote favicon.ico (${ICO_SIZES.join(", ")} px) and apple-icon.png (${APPLE_SIZE} px) in frontend/src/app.`);
