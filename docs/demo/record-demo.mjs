// Records the README demo against the LOCAL docker compose stack (demo seed data). See README.md in this folder.
//
//   ADMIN_PASSWORD=<the local admin password> node record-demo.mjs
//
// Writes the raw video to out/ (git-ignored). docs/demo/README.md explains the ffmpeg step that makes docs/images/demo.gif.
// Only demo accounts are used (their password is public, see the root README). Nothing here talks to production.

import { mkdir, readdir, rename, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const BASE_URL = process.env.DEMO_URL ?? "http://localhost:3000";
const ADMIN_EMAIL = process.env.ADMIN_EMAIL ?? "admin@example.test";
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD;
const DEMO_PASSWORD = "demo-password-1"; // public, only valid for the seeded demo accounts (root README, "Demo data")
const PLAYER_EMAIL = "jogador01@example.test";
const OWNER_EMAIL = "dono01@example.test";
const CLUB_NAME = "Padel Foz Clube"; // owned by dono01 in DemoCatalog
const SPORT_LABEL = "Padel";
const LOCALITY = "Porto";

if (!ADMIN_PASSWORD) {
  console.error("Set ADMIN_PASSWORD to the local admin password (see docs/demo/README.md).");
  process.exit(1);
}
if (!/^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(BASE_URL)) {
  console.error(`Refusing to record against ${BASE_URL}: the demo is recorded on the local stack only.`);
  process.exit(1);
}

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), "out");
await mkdir(outDir, { recursive: true });
for (const name of await readdir(outDir)) await rm(path.join(outDir, name), { recursive: true, force: true }); // keep the folder itself (an editor may hold it)

const SIZE = { width: 1280, height: 720 };

// A visible cursor and a click ripple, drawn by the page itself. The position survives page loads through sessionStorage.
const overlay = () => {
  const install = () => {
    if (document.getElementById("demo-cursor")) return;
    const style = document.createElement("style");
    style.textContent = `
      #demo-cursor { position: fixed; z-index: 2147483647; left: 0; top: 0; width: 22px; height: 22px; pointer-events: none;
        margin: -2px 0 0 -2px; transition: transform 0.02s linear; }
      .demo-ripple { position: fixed; z-index: 2147483646; width: 14px; height: 14px; margin: -7px 0 0 -7px; border-radius: 50%;
        border: 3px solid #2563eb; pointer-events: none; animation: demo-ripple 0.55s ease-out forwards; }
      @keyframes demo-ripple { from { transform: scale(0.4); opacity: 0.9; } to { transform: scale(3.2); opacity: 0; } }`;
    document.head.appendChild(style);
    const cursor = document.createElement("div");
    cursor.id = "demo-cursor";
    cursor.innerHTML =
      '<svg width="22" height="22" viewBox="0 0 22 22"><path d="M2 1 L2 17 L6.5 13 L9.5 20 L12.5 18.7 L9.6 12 L15.5 12 Z" fill="#111" stroke="#fff" stroke-width="1.6" stroke-linejoin="round"/></svg>';
    document.body.appendChild(cursor);
    const place = (x, y) => {
      cursor.style.transform = `translate(${x}px, ${y}px)`;
      try {
        sessionStorage.setItem("demo-cursor", `${x},${y}`);
      } catch {}
    };
    try {
      const saved = sessionStorage.getItem("demo-cursor");
      if (saved) place(...saved.split(",").map(Number));
    } catch {}
    window.addEventListener("mousemove", (event) => place(event.clientX, event.clientY), true);
    window.addEventListener(
      "mousedown",
      (event) => {
        const ripple = document.createElement("div");
        ripple.className = "demo-ripple";
        ripple.style.left = `${event.clientX}px`;
        ripple.style.top = `${event.clientY}px`;
        document.body.appendChild(ripple);
        setTimeout(() => ripple.remove(), 700);
      },
      true,
    );
  };
  if (document.body) install();
  else document.addEventListener("DOMContentLoaded", install);
};

const browser = await chromium.launch({
  headless: false, // headless Chromium shows pt-PT dates as month/day; a headed window shows day/month
  args: ["--disable-extensions", "--hide-scrollbars", "--lang=pt-PT"],
});

// ---- Warm-up (never recorded): visit every route of the flow once in a throwaway context, so the recorded run sees warm
// caches and connection pools and no first-request delays. It signs in but books nothing. ----
{
  const warmContext = await browser.newContext({ viewport: SIZE, locale: "pt-PT", timezoneId: "Europe/Lisbon" });
  const warm = await warmContext.newPage();
  warm.setDefaultTimeout(30_000);
  const login = async (email, password) => {
    await warm.goto(`${BASE_URL}/login`);
    await warm.locator("#login-email").fill(email);
    await warm.locator("#login-password").fill(password);
    await warm.getByRole("button", { name: "Entrar", exact: true }).click();
  };
  const logout = async () => {
    await warm.getByRole("button", { name: "Sair", exact: true }).click();
    await warm.waitForURL(/\/login/);
  };
  await warm.goto(`${BASE_URL}/?sport=padel&locality=Porto`);
  await warm.getByRole("list", { name: "Clubes" }).getByRole("link", { name: new RegExp(CLUB_NAME) }).click();
  await warm.getByRole("list", { name: "Horários" }).waitFor();
  await login("jogador01@example.test", DEMO_PASSWORD);
  await warm.waitForURL(/\/($|\?)/);
  await warm.goto(`${BASE_URL}/bookings`);
  await warm.getByText("As minhas reservas").first().waitFor();
  await logout();
  await login("dono01@example.test", DEMO_PASSWORD);
  await warm.waitForURL(/\/manage/);
  await warm.getByRole("link", { name: new RegExp(`^Calendário.*${CLUB_NAME}`) }).click();
  await warm.waitForURL(/\/calendar/);
  await warm.getByRole("group", { name: /Padel/ }).first().waitFor();
  await logout();
  await login(ADMIN_EMAIL, ADMIN_PASSWORD);
  await warm.waitForURL(/\/admin/);
  await warm.locator(".recharts-bar-rectangle").first().waitFor();
  await warmContext.close();
}

const context = await browser.newContext({
  viewport: SIZE,
  screen: SIZE,
  locale: "pt-PT",
  timezoneId: "Europe/Lisbon",
  colorScheme: "light",
  recordVideo: { dir: outDir, size: SIZE },
});
const recordingStartedAt = Date.now();
await context.addInitScript(overlay);
const page = await context.newPage();
page.setDefaultTimeout(20_000);

let mouse = { x: 640, y: 360 };
const pause = (ms = 1000) => page.waitForTimeout(ms);

async function moveTo(x, y, steps = 20) {
  await page.mouse.move(x, y, { steps });
  mouse = { x, y };
}

/** Moves the cursor smoothly to the middle of the element, clicks it and waits a moment. */
async function click(locator, { after = 400 } = {}) {
  await locator.waitFor({ state: "visible" });
  await locator.scrollIntoViewIfNeeded();
  const box = await locator.boundingBox();
  if (!box) throw new Error("Element has no box");
  await moveTo(box.x + Math.min(box.width / 2, 120), box.y + box.height / 2);
  await pause(150);
  await page.mouse.down();
  await page.mouse.up();
  await pause(after);
}

async function type(locator, text) {
  await click(locator, { after: 100 });
  await locator.pressSequentially(text, { delay: 22 });
  await pause(250);
}

/** A native <select> has no popup in a video: the cursor goes to it, then the option is set. */
async function choose(select, label) {
  await click(select, { after: 150 });
  await select.selectOption({ label });
  await pause(500);
}

async function afterLoad() {
  // The overlay restores the cursor from sessionStorage; make the real mouse agree with it.
  await page.mouse.move(mouse.x, mouse.y);
}

const lisbonDate = (offsetDays) => {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Lisbon", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const [y, m, d] = parts.split("-").map(Number);
  const shifted = new Date(Date.UTC(y, m - 1, d + offsetDays));
  return shifted.toISOString().slice(0, 10);
};
const tomorrow = lisbonDate(1);

async function signIn(email, password) {
  await page.waitForSelector("#login-email");
  await pause(500);
  await type(page.locator("#login-email"), email);
  await type(page.locator("#login-password"), password);
  await click(page.getByRole("button", { name: "Entrar", exact: true }), { after: 0 });
}

async function signOut() {
  const button = page.getByRole("button", { name: "Sair", exact: true });
  await click(button, { after: 0 });
  await page.waitForURL(/\/login/);
  await afterLoad();
}

// ---- Warm-up (not shown): load the app once so the first frame of the video is already the search page. ----
await page.goto(`${BASE_URL}/`);
await page.getByRole("form", { name: "Pesquisar clubes" }).waitFor();
await page.waitForLoadState("networkidle");
await afterLoad();
const trimStart = (Date.now() - recordingStartedAt) / 1000 + 0.2;

// ---- a. Search clubs by sport, locality and date, and open a result ----
await pause(1000);
await choose(page.getByLabel("Desporto"), SPORT_LABEL);
await choose(page.getByLabel("Localidade"), LOCALITY);
await click(page.getByLabel("Data"), { after: 100 });
await page.getByLabel("Data").fill(tomorrow);
await pause(800);
const results = page.getByRole("list", { name: "Clubes" });
await results.waitFor();
await pause(1000);
await click(results.getByRole("link", { name: new RegExp(CLUB_NAME) }), { after: 0 });

// ---- b. Pick the first free slot tomorrow, sign in, come back to it, confirm ----
await page.waitForURL(/\/clubs\//);
await afterLoad();
const slots = page.getByRole("list", { name: "Horários" });
await slots.waitFor();
await pause(800);
const pitchName = (await page.locator("#booking-pitch option:checked").textContent())?.split("·")[0].trim() ?? "";
const firstFree = slots.getByRole("button").and(page.locator(":enabled")).first();
await firstFree.waitFor();
const slotText = ((await firstFree.textContent()) ?? "").replace(/\s+/g, " ").trim();
const startTime = slotText.match(/\d{2}:\d{2}/)?.[0];
if (!startTime) throw new Error(`Could not read the slot time from "${slotText}"`);
await click(firstFree, { after: 700 });
await click(page.getByRole("link", { name: "Entrar para reservar" }), { after: 0 });
await signIn(PLAYER_EMAIL, DEMO_PASSWORD);

// Back on the same club page, with the same slot chosen.
await page.waitForURL(/\/clubs\//);
await afterLoad();
const confirm = page.getByRole("button", { name: "Confirmar reserva" });
await confirm.waitFor();
const playerName = ((await page.getByText(/^Olá, /).first().textContent()) ?? "").replace(/^Olá,\s*/, "").replace(/[.!]$/, "").trim();
await pause(1000);
await click(confirm, { after: 0 });
await page.getByRole("heading", { name: "Reserva confirmada" }).waitFor();
await pause(1800);

// ---- c. My bookings (the "As minhas reservas" page) ----
await click(page.getByRole("link", { name: "As minhas reservas" }).first(), { after: 0 });
await page.waitForURL(/\/bookings/);
await afterLoad();
const myList = page.getByText(CLUB_NAME).first();
await myList.waitFor();
await pause(2200);

// ---- d. Owner: the calendar on the day of that booking ----
await signOut();
await signIn(OWNER_EMAIL, DEMO_PASSWORD);
await page.waitForURL(/\/manage/);
await afterLoad();
await click(page.getByRole("link", { name: new RegExp(`^Calendário.*${CLUB_NAME}`) }), { after: 0 });
await page.waitForURL(/\/calendar/);
await afterLoad();
await page.getByRole("group", { name: pitchName }).first().waitFor();
await pause(800);
const dateField = page.locator('input[type="date"][aria-label="Data"]:visible');
await click(dateField, { after: 100 });
await dateField.fill(tomorrow);
await pause(900);
const startsAt = new RegExp(String.raw`(^|\s)${startTime}\s*[–-]`); // the start time, not an end time that looks the same
const booking = page.locator("[data-booking-id]").filter({ hasText: playerName }).filter({ hasText: startsAt }).first();
await booking.waitFor();
await click(booking, { after: 0 });
await page.getByText("Fechar", { exact: true }).first().waitFor();
await pause(2500);

// ---- e. Admin dashboard ----
await signOut();
await signIn(ADMIN_EMAIL, ADMIN_PASSWORD);
await page.waitForURL(/\/admin/);
await afterLoad();
await page.locator(".recharts-bar-rectangle").first().waitFor();
await page.waitForLoadState("networkidle");
await moveTo(1262, 420, 20); // off the chart, so no tooltip is left on screen
await pause(1800);
await page.mouse.wheel(0, 520);
await pause(2200);

const trimEnd = (Date.now() - recordingStartedAt) / 1000;
await context.close(); // finishes the video file
await browser.close();

const video = (await readdir(outDir)).find((name) => name.endsWith(".webm"));
if (!video) throw new Error("No video was written");
await rename(path.join(outDir, video), path.join(outDir, "demo.webm"));
await writeFile(path.join(outDir, "timing.json"), JSON.stringify({ trimStart: Number(trimStart.toFixed(2)), trimEnd: Number(trimEnd.toFixed(2)) }, null, 2));
console.log(`Recorded out/demo.webm (start at ${trimStart.toFixed(1)} s, ${(trimEnd - trimStart).toFixed(1)} s of flow).`);
