import { expect, type APIRequestContext, type BrowserContext, type Page } from "@playwright/test";

/** Public, and valid only for the seeded demo accounts (root README, "Demo data"). */
export const DEMO_PASSWORD = "demo-password-1";

export const OWNER_EMAIL = "dono01@example.test"; // owns Padel Foz Clube
export const PLAYER_EMAILS = {
  bookAndCancel: "jogador01@example.test",
  firstOfTwo: "jogador02@example.test",
  secondOfTwo: "jogador03@example.test",
  seenByOwner: "jogador04@example.test",
} as const;

export const CLUB = { slug: "padel-foz-clube", name: "Padel Foz Clube", pitchName: "Padel 1" } as const;

/** The seed only books up to 14 days ahead; the tests use dates after that, so they never meet a seeded booking. */
export const FIRST_TEST_DAY = 20;

const ORIGIN = process.env.E2E_BASE_URL ?? "http://localhost:3000";

/** A calendar date in Lisbon (`yyyy-MM-dd`), `offsetDays` after today. */
export function lisbonDate(offsetDays: number): string {
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Lisbon", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  const [year, month, day] = today.split("-").map(Number) as [number, number, number];
  return new Date(Date.UTC(year, month - 1, day + offsetDays)).toISOString().slice(0, 10);
}

/** The calendar date in Lisbon of an instant (`yyyy-MM-dd`). */
export function lisbonDay(instant: string): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Lisbon", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(instant));
}

/** The clock time of an instant in Lisbon, as the pages show it (`HH:mm`). */
export function lisbonTime(instant: string): string {
  return new Intl.DateTimeFormat("pt-PT", { timeZone: "Europe/Lisbon", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(new Date(instant));
}

type Pitch = { id: string; name: string };
type Slot = { startsAt: string; endsAt: string; price: number; available: boolean };
type MyBooking = { id: string; startsAt: string; status: string };

/** Mutating calls need the site's Origin, which a browser sends itself and the test client does not. */
const mutating = { headers: { Origin: ORIGIN } };

/** The demo club's pitch used by every test (read from the API, never a fixed id). */
export async function demoPitch(request: APIRequestContext): Promise<Pitch> {
  const response = await request.get(`/api/clubs/${CLUB.slug}`);
  expect(response.ok(), "the demo club exists: is the demo data seeded?").toBe(true);
  const club = (await response.json()) as { pitches: Pitch[] };
  const pitch = club.pitches.find((candidate) => candidate.name === CLUB.pitchName);
  if (!pitch) throw new Error(`${CLUB.name} has no pitch called ${CLUB.pitchName}`);
  return pitch;
}

export async function slotsOn(request: APIRequestContext, pitchId: string, date: string): Promise<Slot[]> {
  const response = await request.get(`/api/clubs/${CLUB.slug}/pitches/${pitchId}/slots?date=${date}`);
  expect(response.ok()).toBe(true);
  return ((await response.json()) as { slots: Slot[] }).slots;
}

/** The first day from `fromOffset` (counted from today) on which the pitch has a free slot, inside the 30-day booking window. */
export async function firstDayWithFreeSlot(request: APIRequestContext, pitchId: string, fromOffset: number): Promise<{ date: string; slot: Slot }> {
  for (let offset = fromOffset; offset <= 30; offset++) {
    const date = lisbonDate(offset);
    const slot = (await slotsOn(request, pitchId, date)).find((candidate) => candidate.available);
    if (slot) return { date, slot };
  }
  throw new Error(`No free slot between day ${fromOffset} and day 30`);
}

/** Signs in without the form (for the people who are not the subject of the test); the cookies go to the context. Returns the name. */
export async function signInApi(context: BrowserContext, email: string): Promise<string> {
  const response = await context.request.post("/api/auth/login", { ...mutating, data: { email, password: DEMO_PASSWORD } });
  expect(response.ok(), `signing in as ${email}`).toBe(true);
  return ((await response.json()) as { name: string }).name;
}

/** Signs in through the login form, as a person does. */
export async function signInForm(page: Page, email: string): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Palavra-passe").fill(DEMO_PASSWORD);
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page).not.toHaveURL(/\/login/);
}

/** Books through the API (Player session in the context). Returns the booking. */
export async function bookViaApi(context: BrowserContext, pitchId: string, startsAt: string): Promise<{ id: string }> {
  const response = await context.request.post("/api/bookings", { ...mutating, data: { pitchId, startsAt } });
  expect(response.status(), "booking through the API").toBe(201);
  return (await response.json()) as { id: string };
}

/** Every upcoming booking of the signed-in player, all pages. */
export async function upcomingBookings(context: BrowserContext): Promise<{ booking: MyBooking; page: number }[]> {
  const found: { booking: MyBooking; page: number }[] = [];
  for (let page = 1; ; page++) {
    const response = await context.request.get(`/api/me/bookings?scope=upcoming&page=${page}`);
    expect(response.ok()).toBe(true);
    const body = (await response.json()) as { items: MyBooking[]; pageSize: number; totalCount: number };
    found.push(...body.items.map((booking) => ({ booking, page })));
    if (page * body.pageSize >= body.totalCount) return found;
  }
}

/**
 * Cancels the signed-in player's confirmed bookings from `FIRST_TEST_DAY` on, which only these tests make. Run at the start of a test
 * (a crashed run leaves nothing behind that matters) and at its end, so the tests can run again against the same database.
 */
export async function cancelTestBookings(context: BrowserContext): Promise<void> {
  const from = lisbonDate(FIRST_TEST_DAY);
  for (const { booking } of await upcomingBookings(context)) {
    if (booking.status !== "confirmed" || lisbonDay(booking.startsAt) < from) continue;
    const response = await context.request.post(`/api/bookings/${booking.id}/cancel`, mutating);
    expect(response.ok(), "cancelling a test booking").toBe(true);
  }
}
