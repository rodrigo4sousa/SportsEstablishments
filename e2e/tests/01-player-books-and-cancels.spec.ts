import { expect, test, type Page } from "@playwright/test";
import { cancelTestBookings, CLUB, demoPitch, FIRST_TEST_DAY, firstDayWithFreeSlot, lisbonTime, PLAYER_EMAILS, signInForm, upcomingBookings } from "../support/demo";

// Flow 1: a player signs in, books a free slot of the demo club, sees the booking in "As minhas reservas" and cancels it.
test("a player books a free slot, sees it in my bookings and cancels it", async ({ page }) => {
  const pitch = await demoPitch(page.request);
  const { date, slot } = await firstDayWithFreeSlot(page.request, pitch.id, FIRST_TEST_DAY);
  const start = lisbonTime(slot.startsAt);

  await signInForm(page, PLAYER_EMAILS.bookAndCancel);
  await cancelTestBookings(page.context());

  try {
    await test.step("book the slot on the club page", async () => {
      await page.goto(`/clubs/${CLUB.slug}`);
      await page.getByLabel("Campo", { exact: true }).selectOption(pitch.id);
      await page.getByLabel("Data").fill(date);
      await page.getByRole("list", { name: "Horários" }).getByRole("button", { name: new RegExp(`^${start}`) }).click();
      await page.getByRole("button", { name: "Confirmar reserva" }).click();
      await expect(page.getByRole("heading", { name: "Reserva confirmada" })).toBeVisible();
    });

    await test.step("see it in my bookings", async () => {
      // The list is paged (20 to a page) and ordered by start; the API says which page holds this booking.
      const mine = (await upcomingBookings(page.context())).find(({ booking }) => booking.startsAt === slot.startsAt);
      expect(mine, "the new booking is in the upcoming list").toBeDefined();
      await page.goto(`/bookings?page=${mine!.page}`);
      await expect(bookingItem(page, date, start)).toBeVisible();
      await expect(bookingItem(page, date, start)).toContainText("Confirmada");
    });

    await test.step("cancel it", async () => {
      await bookingItem(page, date, start).getByRole("button", { name: "Cancelar reserva" }).click();
      await page.getByRole("button", { name: "Sim, cancelar reserva" }).click();
      await expect(page.getByText("Reserva cancelada. O horário voltou a ficar livre.")).toBeVisible();
      const left = (await upcomingBookings(page.context())).filter(({ booking }) => booking.startsAt === slot.startsAt);
      expect(left.filter(({ booking }) => booking.status === "confirmed")).toHaveLength(0);
    });
  } finally {
    await cancelTestBookings(page.context());
  }
});

/** The row of the booking that starts at `start` on `date`: the page groups bookings by day, and each day's list is named by its heading (`bookings-day-<date>`). */
function bookingItem(page: Page, date: string, start: string) {
  return page
    .locator(`ul[aria-labelledby="bookings-day-${date}"]`)
    .getByRole("listitem", { name: `${CLUB.name}, ${CLUB.pitchName}` })
    .filter({ hasText: new RegExp(`${start}\\s*–`) });
}
