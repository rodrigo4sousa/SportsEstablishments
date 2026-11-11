import { renderWithIntl } from "@/test/intl";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { PublicSlot } from "@/features/catalog/types";
import { SlotList } from "./slot-list";

const slots: PublicSlot[] = [
  { startsAt: "2026-05-04T08:00:00Z", endsAt: "2026-05-04T09:00:00Z", price: 18.5, available: true },
  { startsAt: "2026-05-04T09:00:00Z", endsAt: "2026-05-04T10:00:00Z", price: 18.5, available: false },
  { startsAt: "2026-05-04T10:00:00Z", endsAt: "2026-05-04T11:00:00Z", price: 18.5, available: true },
];

const text = (element: HTMLElement) => (element.textContent ?? "").replace(/\s/g, " ");

describe("SlotList", () => {
  it("shows each slot in Lisbon time (summer: UTC+1) with its price in pt-PT format", () => {
    renderWithIntl(<SlotList slots={slots} selected={null} interactive onSelect={() => undefined} />);

    const items = within(screen.getByRole("list", { name: "Horários" })).getAllByRole("listitem");
    expect(items.map(text)).toEqual(["09:00 – 10:00 18,50 €", "10:00 – 11:00 18,50 €", "11:00 – 12:00 18,50 €"]);
  });

  it("shows the price of each slot, which differs inside and outside a price band (US10)", () => {
    const banded: PublicSlot[] = [
      { startsAt: "2026-05-04T08:00:00Z", endsAt: "2026-05-04T09:00:00Z", price: 20, available: true },
      { startsAt: "2026-05-04T09:00:00Z", endsAt: "2026-05-04T10:00:00Z", price: 35.5, available: true },
      { startsAt: "2026-05-04T10:00:00Z", endsAt: "2026-05-04T11:00:00Z", price: 35.5, available: true },
      { startsAt: "2026-05-04T11:00:00Z", endsAt: "2026-05-04T12:00:00Z", price: 20, available: true },
    ];
    renderWithIntl(<SlotList slots={banded} selected={null} interactive onSelect={() => undefined} />);

    const items = within(screen.getByRole("list", { name: "Horários" })).getAllByRole("listitem");
    expect(items.map(text)).toEqual(["09:00 – 10:00 20,00 €", "10:00 – 11:00 35,50 €", "11:00 – 12:00 35,50 €", "12:00 – 13:00 20,00 €"]);
  });

  it("has a legend for the two looks, which says that a slot is unavailable and never why (UX-025)", () => {
    renderWithIntl(<SlotList slots={slots} selected={null} interactive onSelect={() => {}} />);

    expect(screen.getByText("Livre")).toBeInTheDocument();
    expect(screen.getByText("Indisponível (riscado)")).toBeInTheDocument();
  });

  it("strikes the time of an unavailable slot, and keeps its text as dark as muted text allows instead of fading it (UX-025)", () => {
    renderWithIntl(<SlotList slots={slots} selected={null} interactive onSelect={() => {}} />);

    const [free, unavailable] = screen.getAllByRole("button");
    expect(unavailable.querySelector(".line-through")).not.toBeNull();
    expect(free.querySelector(".line-through")).toBeNull();
    expect(unavailable.className).not.toMatch(/opacity/);
  });

  it("shows the same legend and looks to someone who can only look (an owner or an admin)", () => {
    renderWithIntl(<SlotList slots={slots} selected={null} interactive={false} onSelect={() => {}} />);

    expect(screen.getByText("Indisponível (riscado)")).toBeInTheDocument();
    expect(document.querySelector('[data-available="false"]')?.className).not.toMatch(/opacity/);
  });

  it("makes free slots buttons and shows the others disabled, with no reason", async () => {
    const onSelect = vi.fn();
    renderWithIntl(<SlotList slots={slots} selected={null} interactive onSelect={onSelect} />);

    const [free, unavailable, other] = screen.getAllByRole("button");
    expect(free).toBeEnabled();
    expect(unavailable).toBeDisabled();
    expect(other).toBeEnabled();
    // Nothing says why: no "reservado", "bloqueado", "fechado" or "começou" anywhere.
    expect(text(unavailable)).toBe("10:00 – 11:00 18,50 €");
    expect(screen.queryByText(/reservad|bloquead|fechad|começ/i)).not.toBeInTheDocument();

    await userEvent.click(unavailable);
    expect(onSelect).not.toHaveBeenCalled();
    await userEvent.click(free);
    expect(onSelect).toHaveBeenCalledExactlyOnceWith(slots[0]);
  });

  it("marks the chosen slot, comparing moments and not text", () => {
    renderWithIntl(<SlotList slots={slots} selected="2026-05-04T11:00:00+01:00" interactive onSelect={() => undefined} />);

    const [first, , third] = screen.getAllByRole("button");
    expect(first).toHaveAttribute("aria-pressed", "false");
    expect(third).toHaveAttribute("aria-pressed", "true");
  });

  it("labels the two 01:00 hours of the night clocks go back, and tells their slots apart", () => {
    // 25 October 2026: 00:00-01:00 UTC is 01:00-02:00 WEST (summer), then 01:00-02:00 UTC is 01:00-02:00 WET (winter).
    const repeated: PublicSlot[] = [
      { startsAt: "2026-10-25T00:00:00Z", endsAt: "2026-10-25T01:00:00Z", price: 10, available: true },
      { startsAt: "2026-10-25T01:00:00Z", endsAt: "2026-10-25T02:00:00Z", price: 10, available: true },
    ];

    renderWithIntl(<SlotList slots={repeated} selected={null} interactive onSelect={() => undefined} />);

    const [first, second] = screen.getAllByRole("button").map(text);
    expect(first).toBe("01:00 (verão) – 01:00 (inverno) 10,00 €");
    expect(second).toBe("01:00 (inverno) – 02:00 10,00 €");
  });

  it("shows free slots as plain items, not buttons, for someone who cannot book", () => {
    renderWithIntl(<SlotList slots={slots} selected={null} interactive={false} onSelect={() => undefined} />);

    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    const items = screen.getAllByRole("listitem");
    expect(items.map((item) => item.firstElementChild?.getAttribute("data-available"))).toEqual(["true", "false", "true"]);
  });
});
