// Takes the README screenshots (docs/images/screenshot-*.png) in English against the LOCAL docker compose stack with the demo data.
// See README.md in this folder.
//
//   ADMIN_PASSWORD=<the local admin password> node capture-screenshots.mjs
//
// It changes the database: it adds a price band, a block and a customer booking through the API (as the demo owners) and a weekly
// series through the interface (as jogador01), so it needs a NEW database, like the GIF recorder. Free text that it creates is
// English. Only demo accounts are used (their password is public, see the root README). Nothing here talks to production.

import { execFileSync } from "node:child_process";
import { mkdir, readdir, rename, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const BASE_URL = process.env.DEMO_URL ?? "http://localhost:3000";
const ADMIN_EMAIL = process.env.ADMIN_EMAIL ?? "admin@example.test";
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD;
const DEMO_PASSWORD = "demo-password-1"; // public, only valid for the seeded demo accounts (root README, "Demo data")
const PLAYER_EMAIL = "jogador01@example.test";
const FUTSAL_CLUB = "Arena Futsal Boavista";
const PADEL_CLUB = "Lisboa Padel Club";
const STATS_CLUB = "Padel Foz Clube";
const LOCALE = "en-GB";
const SIZE = { width: 1440, height: 900 };
// A period entirely inside the seeded data (it covers the 60 days before the seed and the 14 after), for the statistics screens.
const STATS_FROM = "2026-09-15";
const STATS_TO = "2026-10-14";

if (!ADMIN_PASSWORD) {
  console.error("Set ADMIN_PASSWORD to the local admin password (see docs/demo/README.md).");
  process.exit(1);
}
if (!/^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(BASE_URL)) {
  console.error(`Refusing to capture against ${BASE_URL}: screenshots are taken on the local stack only.`);
  process.exit(1);
}

const here = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.join(here, "out", "screenshots");
const imagesDir = path.join(here, "..", "images");
await rm(outDir, { recursive: true, force: true });
await mkdir(outDir, { recursive: true });

// Headless Chromium and every en-* browser language show dates as month/day; a headed window with a pt-PT shell shows dd/mm/yyyy
// (see README.md). The site is English because of the NEXT_LOCALE cookie and the en-GB context locale.
const browser = await chromium.launch({ headless: false, args: ["--disable-extensions", "--hide-scrollbars", "--lang=pt-PT"] });

async function newContext() {
  const context = await browser.newContext({ viewport: SIZE, screen: SIZE, locale: LOCALE, timezoneId: "Europe/Lisbon", colorScheme: "light" });
  await context.addCookies([{ name: "NEXT_LOCALE", value: "en", url: BASE_URL }]);
  return context;
}

async function signedIn(email, password) {
  const context = await newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(30_000);
  await page.goto(`${BASE_URL}/login`);
  await page.locator("#login-email").fill(email);
  await page.locator("#login-password").fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.waitForURL((url) => !url.pathname.startsWith("/login"));
  return { context, page };
}

/** An API call with the session cookies of the context. The API checks the Origin of every mutating request. */
async function api(context, method, url, body) {
  const response = await context.request.fetch(`${BASE_URL}${url}`, {
    method,
    headers: { Origin: BASE_URL, "Content-Type": "application/json" },
    data: body === undefined ? undefined : JSON.stringify(body),
    failOnStatusCode: false,
  });
  const text = await response.text();
  return { status: response.status(), body: text ? JSON.parse(text) : null };
}

async function apiOk(context, method, url, body) {
  const result = await api(context, method, url, body);
  if (result.status >= 300) throw new Error(`${method} ${url} answered ${result.status}: ${JSON.stringify(result.body)}`);
  return result.body;
}

const lisbonDate = (offsetDays) => {
  const [y, m, d] = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Lisbon", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date()).split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d + offsetDays)).toISOString().slice(0, 10);
};
const weekdayOf = (dateKey) => new Date(`${dateKey}T12:00:00Z`).getUTCDay(); // 0 = Sunday
const localTime = (instant) => instant.match(/T(\d{2}:\d{2})/)?.[1]; // the API gives instants with the Lisbon offset

const findClub = async (context, name) => {
  const clubs = (await apiOk(context, "GET", "/api/manage/clubs?pageSize=100")).items;
  return clubs.find((club) => club.name === name);
};
const pitchesOf = async (context, clubId) => (await apiOk(context, "GET", `/api/manage/clubs/${clubId}/pitches?pageSize=100`)).items;

// ---- Setup, in English, through the API as the demo owners (before any screenshot, so every image shows it) ----
const owner1 = await signedIn("dono01@example.test", DEMO_PASSWORD); // Arena Futsal Boavista and Padel Foz Clube
const owner4 = await signedIn("dono04@example.test", DEMO_PASSWORD); // Lisboa Padel Club
const futsalClub = await findClub(owner1.context, FUTSAL_CLUB);
const statsClub = await findClub(owner1.context, STATS_CLUB);
const padelClub = await findClub(owner4.context, PADEL_CLUB);
if (!futsalClub || !statsClub || !padelClub) throw new Error("A demo club was not found: is this the seed-demo database?");
const futsalA = (await pitchesOf(owner1.context, futsalClub.id)).find((pitch) => pitch.name === "Futsal A");
const padelPitches = await pitchesOf(owner4.context, padelClub.id);
const padel1 = padelPitches.find((pitch) => pitch.name === "Padel 1");
const padel2 = padelPitches.find((pitch) => pitch.name === "Padel 2");
if (!futsalA || !padel1 || !padel2) throw new Error("A demo pitch was not found");

const publicSlots = async (date) => (await apiOk(owner1.context, "GET", `/api/clubs/${futsalClub.slug}/pitches/${futsalA.id}/slots?date=${date}`)).slots;
const freeAt21 = async (date) => (await publicSlots(date)).find((slot) => localTime(slot.startsAt) === "21:00" && slot.available);

// The day for most screenshots: a weekday (the price band covers Monday to Friday) on which Futsal A is free at 21:00 that day, the
// next week and the week after (the weekly series), and on which Lisboa Padel Club can get the block and the customer booking.
let day = null;
let series21 = null;
for (let offset = 1; offset <= 14 && !day; offset++) {
  const date = lisbonDate(offset);
  if (weekdayOf(date) === 0 || weekdayOf(date) === 6) continue;
  const slot = await freeAt21(date);
  if (!slot || !(await freeAt21(lisbonDate(offset + 7))) || !(await freeAt21(lisbonDate(offset + 14)))) continue;

  const block = await api(owner4.context, "POST", `/api/manage/clubs/${padelClub.id}/pitches/${padel1.id}/blocks`, {
    start: `${date}T09:00`,
    end: `${date}T12:00`,
    reason: "Floodlight maintenance",
  });
  if (block.status !== 201) continue; // confirmed bookings in the way
  const slots = (await apiOk(owner4.context, "GET", `/api/manage/clubs/${padelClub.id}/pitches/${padel2.id}/slots?date=${date}`)).slots;
  const afternoon = slots.find((s) => localTime(s.startsAt) === "15:00" && !s.booked && !s.blocked);
  const booking = afternoon
    ? await api(owner4.context, "POST", `/api/manage/clubs/${padelClub.id}/bookings`, {
        pitchId: padel2.id,
        startsAt: afternoon.startsAt,
        customerName: "Rui Teixeira",
        customerPhone: "912345678",
      })
    : { status: 0 };
  if (booking.status !== 201) {
    await api(owner4.context, "DELETE", `/api/manage/clubs/${padelClub.id}/pitches/${padel1.id}/blocks/${block.body.id}`);
    continue;
  }
  day = date;
  series21 = slot;
}
if (!day) throw new Error("No day fits the screenshots; use a new database (see README.md).");
console.log(`Day used: ${day}`);

await apiOk(owner1.context, "POST", `/api/manage/clubs/${futsalClub.id}/pitches/${futsalA.id}/price-bands`, {
  days: ["monday", "tuesday", "wednesday", "thursday", "friday"],
  start: "18:00",
  end: "00:00",
  price: 46,
});
series21 = await freeAt21(day); // the price now comes from the band

// ---- Taking the pictures ----
const raw = (name) => path.join(outDir, `${name}.png`);
async function settle(page) {
  await page.waitForLoadState("networkidle");
  await page.evaluate(() => document.fonts.ready);
  await page.mouse.move(0, 0);
  await page.waitForTimeout(400);
}
async function shoot(page, name, options = {}) {
  await settle(page);
  await page.screenshot({ path: raw(name), fullPage: true, ...options });
}

// Visitor
{
  const context = await newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(30_000);

  await page.goto(`${BASE_URL}/?sport=padel&date=${day}`);
  await page.getByRole("list", { name: "Clubs" }).waitFor();
  await page.getByText("Free times available").first().waitFor();
  await shoot(page, "screenshot-club-search-results");

  await page.goto(`${BASE_URL}/clubs/${futsalClub.slug}?pitch=${futsalA.id}&date=${day}`);
  await page.getByRole("list", { name: "Times" }).getByRole("button").first().waitFor();
  await shoot(page, "screenshot-club-page-slots-and-prices");
  await context.close();
}

// Player: the summary with the weekly repeat, then the series itself, then "My bookings"
{
  const { context, page } = await signedIn(PLAYER_EMAIL, DEMO_PASSWORD);
  const slot = encodeURIComponent(series21.startsAt);
  await page.goto(`${BASE_URL}/clubs/${futsalClub.slug}?pitch=${futsalA.id}&date=${day}&slot=${slot}&weeks=3`);
  await page.getByText("All 3 dates are free.").waitFor();
  await page.getByRole("list", { name: "Dates of the weekly booking" }).waitFor();
  await shoot(page, "screenshot-booking-summary-weekly-repeat");

  await page.getByRole("button", { name: "Confirm 3 bookings" }).click();
  await page.getByText("3 bookings confirmed").first().waitFor();

  await page.goto(`${BASE_URL}/bookings`);
  await page.getByText(/Weekly booking: 3 of 3/).first().waitFor();
  await shoot(page, "screenshot-my-bookings-upcoming-series");
  await context.close();
}

// Owner of Lisboa Padel Club: the calendar of that day
{
  const page = owner4.page;
  await page.goto(`${BASE_URL}/manage/clubs/${padelClub.id}/calendar?date=${day}`);
  await page.locator("[data-booking-id]").first().waitFor();
  await page.getByText("Floodlight maintenance").first().waitFor();
  await shoot(page, "screenshot-owner-calendar-day");
}

// Owner of Padel Foz Clube: statistics of a period inside the data
{
  const page = owner1.page;
  await page.goto(`${BASE_URL}/manage/clubs/${statsClub.id}/statistics?from=${STATS_FROM}&to=${STATS_TO}`);
  await page.getByRole("heading", { name: /statistics/i }).first().waitFor();
  await page.getByText("Occupation").first().waitFor();
  await shoot(page, "screenshot-owner-statistics-club");
}

// Admin: the dashboard, and a crop of it
{
  const { context, page } = await signedIn(ADMIN_EMAIL, ADMIN_PASSWORD);
  await page.goto(`${BASE_URL}/admin?from=${STATS_FROM}&to=${STATS_TO}`);
  await page.locator(".recharts-bar-rectangle").first().waitFor();
  await page.getByRole("heading", { name: "Clubs with the highest occupation" }).waitFor();
  await page.getByRole("table").first().waitFor();
  await page.waitForTimeout(800); // the charts animate in
  await shoot(page, "screenshot-admin-dashboard");

  const top = (await page.getByRole("heading", { name: "New registrations" }).boundingBox()).y + (await page.evaluate(() => window.scrollY));
  // From a little above the registrations heading to a little below the last row of the top-clubs table (not cut off).
  const tableBox = await page.locator("table").filter({ hasText: "Booked hours" }).first().boundingBox();
  const scrollY = await page.evaluate(() => window.scrollY);
  const y = Math.max(0, top - 40);
  const crop = { x: 0, y, width: SIZE.width, height: Math.ceil(tableBox.y + scrollY + tableBox.height + 40 - y) };
  await settle(page);
  await page.screenshot({ path: raw("screenshot-admin-top-occupation-registrations"), fullPage: true, clip: crop });
  await context.close();
}

await owner1.context.close();
await owner4.context.close();
await browser.close();

// Each PNG is reduced to a palette of 128 colours, like the ones before it, and written to docs/images.
for (const file of (await readdir(outDir)).filter((name) => name.endsWith(".png"))) {
  const target = path.join(imagesDir, file);
  execFileSync(
    "ffmpeg",
    ["-v", "error", "-y", "-i", path.join(outDir, file), "-vf", "split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=none", `${target}.tmp.png`],
    { stdio: "inherit" },
  );
  await rename(`${target}.tmp.png`, target);
  console.log(`Wrote docs/images/${file}`);
}
