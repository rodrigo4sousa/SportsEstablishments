import { expect, test } from "@playwright/test";
import { cancelTestBookings, CLUB, demoPitch, FIRST_TEST_DAY, firstDayWithFreeSlot, lisbonTime, PLAYER_EMAILS, signInApi, slotsOn, upcomingBookings } from "../support/demo";

// Flow 2: two players have the same free slot on their screens. The first books it; the second gets the stable conflict (409
// `slot_taken`), the page says so, and only one booking exists. (The database guarantee under real concurrency is the backend's test.)
test("two players want the same slot: the second one gets the conflict", async ({ browser }) => {
  const first = await browser.newContext();
  const second = await browser.newContext();
  try {
    const pitch = await demoPitch(first.request);
    // One day after the first test's day when it can, so the two tests do not look at the same slot.
    const { date, slot } = await firstDayWithFreeSlot(first.request, pitch.id, FIRST_TEST_DAY + 1);
    const start = lisbonTime(slot.startsAt);

    await signInApi(first, PLAYER_EMAILS.firstOfTwo);
    await signInApi(second, PLAYER_EMAILS.secondOfTwo);
    await cancelTestBookings(first);
    await cancelTestBookings(second);

    const firstPage = await first.newPage();
    const secondPage = await second.newPage();

    // Both see the slot free and choose it.
    for (const page of [firstPage, secondPage]) {
      await page.goto(`/clubs/${CLUB.slug}`);
      await page.getByLabel("Campo", { exact: true }).selectOption(pitch.id);
      await page.getByLabel("Data").fill(date);
      await page.getByRole("list", { name: "Horários" }).getByRole("button", { name: new RegExp(`^${start}`) }).click();
      await expect(page.getByRole("button", { name: "Confirmar reserva" })).toBeEnabled();
    }

    await test.step("the first player books it", async () => {
      await firstPage.getByRole("button", { name: "Confirmar reserva" }).click();
      await expect(firstPage.getByRole("heading", { name: "Reserva confirmada" })).toBeVisible();
    });

    await test.step("the second player gets the conflict", async () => {
      const answer = secondPage.waitForResponse((response) => response.url().endsWith("/api/bookings") && response.request().method() === "POST");
      await secondPage.getByRole("button", { name: "Confirmar reserva" }).click();
      const response = await answer;
      expect(response.status()).toBe(409);
      expect(((await response.json()) as { code: string }).code).toBe("slot_taken");

      await expect(secondPage.getByText("Este horário acabou de ser reservado. Escolhe outro.")).toBeVisible();
      await expect(secondPage.getByRole("heading", { name: "Reserva confirmada" })).toHaveCount(0);
      // The page reloads the slots: the slot is now taken.
      await expect(secondPage.getByRole("list", { name: "Horários" }).getByRole("button", { name: new RegExp(`^${start}`) })).toBeDisabled();
    });

    await test.step("only one booking exists", async () => {
      const taken = (await slotsOn(first.request, pitch.id, date)).find((candidate) => candidate.startsAt === slot.startsAt);
      expect(taken?.available).toBe(false);
      const confirmedAt = async (context: typeof first) =>
        (await upcomingBookings(context)).filter(({ booking }) => booking.startsAt === slot.startsAt && booking.status === "confirmed");
      expect(await confirmedAt(first)).toHaveLength(1);
      expect(await confirmedAt(second)).toHaveLength(0);
    });
  } finally {
    // The sessions exist unless signing in itself failed; then there is nothing to clean up.
    await cancelTestBookings(first).catch(() => undefined);
    await cancelTestBookings(second).catch(() => undefined);
    await first.close();
    await second.close();
  }
});
