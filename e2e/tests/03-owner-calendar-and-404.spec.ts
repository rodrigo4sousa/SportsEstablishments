import { expect, test } from "@playwright/test";
import { bookViaApi, cancelTestBookings, CLUB, demoPitch, FIRST_TEST_DAY, firstDayWithFreeSlot, lisbonTime, OWNER_EMAIL, PLAYER_EMAILS, signInApi, signInForm } from "../support/demo";

// Flow 3: an owner signs in and finds a player's booking in the club calendar; and an address that does not exist is a real 404.
test("an owner sees a booking in the calendar, and an unknown club is a 404", async ({ browser, page }) => {
  const playerContext = await browser.newContext();
  try {
    const pitch = await demoPitch(page.request);
    const { date, slot } = await firstDayWithFreeSlot(page.request, pitch.id, FIRST_TEST_DAY + 2);
    const start = lisbonTime(slot.startsAt);

    // The booking is made for the owner to find; the player is not what this test is about, so no form.
    const playerName = await signInApi(playerContext, PLAYER_EMAILS.seenByOwner);
    await cancelTestBookings(playerContext);
    await bookViaApi(playerContext, pitch.id, slot.startsAt);

    await test.step("the owner opens the calendar of the club", async () => {
      await signInForm(page, OWNER_EMAIL);
      await expect(page).toHaveURL(/\/manage/);
      await page.getByRole("link", { name: new RegExp(`^Calendário.*${CLUB.name}`) }).click();
      await expect(page).toHaveURL(/\/calendar/);
      await page.locator('input[type="date"][aria-label="Data"]:visible').fill(date);
    });

    await test.step("the booking is there, with the player's name", async () => {
      const box = page.getByRole("button", { name: new RegExp(`${playerName}.*${start}\\s*[–-]`) });
      await expect(box).toBeVisible();
      await expect(box).toContainText("Confirmada");
      await box.click();
      await expect(page.getByText("Fechar", { exact: true }).first()).toBeVisible();
    });

    await test.step("an address that does not exist answers 404", async () => {
      const response = await page.request.get("/clubs/clube-que-nao-existe");
      expect(response.status()).toBe(404);
      await page.goto("/clubs/clube-que-nao-existe");
      await expect(page.getByRole("heading", { name: "Página não encontrada" })).toBeVisible();
    });
  } finally {
    await cancelTestBookings(playerContext).catch(() => undefined);
    await playerContext.close();
  }
});
