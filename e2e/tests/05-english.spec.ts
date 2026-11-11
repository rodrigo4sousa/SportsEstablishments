import { expect, test } from "@playwright/test";
import { cancelTestBookings, CLUB, DEMO_PASSWORD, demoPitch, FIRST_TEST_DAY, firstDayWithFreeSlot, lisbonTime, PLAYER_EMAILS } from "../support/demo";

// Flow 5 (T09): the browser asks for Portuguese (the config's locale); the person switches to English in the header, the labels change,
// the choice survives a reload (the NEXT_LOCALE cookie), and signing in and booking work in English.
test("a player switches the site to English, keeps it after a reload and books in English", async ({ page }) => {
  const pitch = await demoPitch(page.request);
  const { date, slot } = await firstDayWithFreeSlot(page.request, pitch.id, FIRST_TEST_DAY);
  const start = lisbonTime(slot.startsAt);
  // The clean-up reads the player's bookings, so it only runs once the player is signed in.
  let signedIn = false;

  try {
    await test.step("switch to English in the header", async () => {
      await page.goto("/");
      await expect(page.getByRole("heading", { level: 1, name: "Encontra onde jogar" })).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("lang", "pt-PT");

      const switcher = page.getByRole("banner").getByRole("group", { name: "Idioma" });
      await switcher.getByRole("button", { name: "EN – English" }).click();

      await expect(page.getByRole("heading", { level: 1, name: "Find where to play" })).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("lang", "en");
      await expect(page.getByRole("banner").getByRole("button", { name: "EN – English" })).toHaveAttribute("aria-current", "true");
      const cookie = (await page.context().cookies()).find((candidate) => candidate.name === "NEXT_LOCALE");
      expect(cookie?.value).toBe("en");
    });

    await test.step("keep English after a reload", async () => {
      await page.reload();
      await expect(page.getByRole("heading", { level: 1, name: "Find where to play" })).toBeVisible();
      await expect(page.getByRole("contentinfo").getByRole("link", { name: "Privacy Policy" })).toBeVisible();
    });

    await test.step("sign in with the English form", async () => {
      await page.goto("/login");
      await page.getByLabel("Email").fill(PLAYER_EMAILS.english);
      await page.getByLabel("Password").fill(DEMO_PASSWORD);
      await page.getByRole("button", { name: "Sign in", exact: true }).click();
      await expect(page).not.toHaveURL(/\/login/);
      signedIn = true;
      await cancelTestBookings(page.context());
    });

    await test.step("book a slot in English", async () => {
      await page.goto(`/clubs/${CLUB.slug}`);
      await page.getByLabel("Pitch", { exact: true }).selectOption(pitch.id);
      await page.getByLabel("Date").fill(date);
      await expect(page.getByText("Times are Lisbon time.")).toBeVisible();
      await page.getByRole("list", { name: "Times" }).getByRole("button", { name: new RegExp(`^${start}`) }).click();
      await page.getByRole("button", { name: "Confirm booking" }).click();
      await expect(page.getByRole("heading", { name: "Booking confirmed" })).toBeVisible();
    });
  } finally {
    if (signedIn) await cancelTestBookings(page.context());
  }
});
