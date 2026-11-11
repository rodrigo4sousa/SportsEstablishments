import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SiteFooter } from "@/components/site-footer";
import { SlotList } from "@/features/booking/components/slot-list";
import { renderWithIntl } from "@/test/intl";

/** T09: the same components, in English, with British formats and Lisbon times. */
describe("the site in English", () => {
  it("shows a slot's time in Lisbon, the repeated October hour in words and the price the en-GB way", () => {
    renderWithIntl(
      <SlotList
        slots={[
          { startsAt: "2026-10-25T00:00:00Z", endsAt: "2026-10-25T01:00:00Z", price: 18.5, available: true },
          { startsAt: "2026-10-25T01:00:00Z", endsAt: "2026-10-25T02:00:00Z", price: 18.5, available: false },
        ]}
        selected={null}
        interactive
        onSelect={() => {}}
      />,
      "en",
    );

    expect(screen.getByRole("list", { name: "Times" })).toBeInTheDocument();
    expect(screen.getByText("01:00 (summer time) – 01:00 (winter time)")).toBeInTheDocument();
    expect(screen.getAllByText("€18.50")).toHaveLength(2);
    expect(screen.getByText("Times are Lisbon time.")).toBeInTheDocument();
    expect(screen.getByText("Free")).toBeInTheDocument();
  });

  it("names the legal pages in English, next to the switcher", () => {
    renderWithIntl(<SiteFooter />, "en");

    const legal = screen.getByRole("navigation", { name: "Legal information" });
    expect(legal).toHaveTextContent("Terms and Conditions");
    expect(legal).toHaveTextContent("Privacy Policy");
    expect(screen.getByRole("group", { name: "Language" })).toBeInTheDocument();
  });
});
