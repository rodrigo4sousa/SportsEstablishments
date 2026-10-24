import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicClub, PublicSlot } from "@/features/catalog/types";
import { currentPath, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ClubBooking } from "./club-booking";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const padel = "0199f7a0-0000-7000-8000-0000000000d1";
const tennis = "0199f7a0-0000-7000-8000-0000000000d2";

const club: PublicClub = {
  slug: "clube-da-foz",
  name: "Clube da Foz",
  address: "Rua do Mar 1",
  locality: "Porto",
  phone: "912345678",
  contactEmail: "geral@foz.pt",
  pitches: [
    { id: padel, name: "Padel 1", sport: "padel", pricePerSlot: 18.5 },
    { id: tennis, name: "Ténis 1", sport: "tennis", pricePerSlot: 12 },
  ],
};

const slot = (startsAt: string, endsAt: string, available = true, price = 18.5): PublicSlot => ({ startsAt, endsAt, price, available });

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

type Asked = { pitchId: string; date: string };

/** Serves the slots of every pitch; returns what the page asked for. */
function serveSlots(respond: (asked: Asked) => Response = ({ date }) => HttpResponse.json({ date, slots: [slot("2026-05-01T14:00:00Z", "2026-05-01T15:00:00Z")] })) {
  const asked: Asked[] = [];
  server.use(
    http.get("/api/clubs/clube-da-foz/pitches/:pitchId/slots", ({ request, params }) => {
      const item = { pitchId: String(params.pitchId), date: new URL(request.url).searchParams.get("date") ?? "" };
      asked.push(item);
      return respond(item);
    }),
  );
  return asked;
}

const pitchField = () => screen.getByLabelText("Campo") as HTMLSelectElement;
const dateField = () => screen.getByLabelText("Data") as HTMLInputElement;
const times = () => within(screen.getByRole("list", { name: "Horários" })).getAllByRole("listitem").map((item) => (item.textContent ?? "").replace(/\s/g, " "));

describe("ClubBooking: the slots of a pitch", () => {
  beforeEach(() => {
    // 10:00 UTC on Friday 1 May 2026 is 11:00 in Lisbon.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-01T10:00:00Z"));
    resetNavigation("/clubs/clube-da-foz");
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts on the first pitch and today, with the date limited to today..+30 days", async () => {
    const asked = serveSlots();

    renderWithProviders(<ClubBooking club={club} />);

    await screen.findByRole("list", { name: "Horários" });
    expect(asked).toEqual([{ pitchId: padel, date: "2026-05-01" }]);
    expect(pitchField()).toHaveValue(padel);
    expect(within(pitchField()).getAllByRole("option").map((option) => (option.textContent ?? "").replace(/\s/g, " "))).toEqual([
      "Padel 1 · Padel · 18,50 €",
      "Ténis 1 · Ténis · 12,00 €",
    ]);
    expect(dateField()).toHaveValue("2026-05-01");
    expect(dateField()).toHaveAttribute("min", "2026-05-01");
    expect(dateField()).toHaveAttribute("max", "2026-05-31");
  });

  it("lists the slots in Lisbon time with their price", async () => {
    serveSlots(({ date }) =>
      HttpResponse.json({
        date,
        slots: [
          slot("2026-05-01T14:00:00Z", "2026-05-01T15:00:00Z"),
          slot("2026-05-01T15:00:00Z", "2026-05-01T16:00:00Z", false),
        ],
      }),
    );

    renderWithProviders(<ClubBooking club={club} />);

    await screen.findByRole("list", { name: "Horários" });
    expect(times()).toEqual(["15:00 – 16:00 18,50 €", "16:00 – 17:00 18,50 €"]);
    expect(screen.getByText("sexta-feira, 1 de maio")).toBeInTheDocument();
  });

  it("uses the Lisbon date as today, not the UTC date", async () => {
    // 23:30 UTC on Sunday 3 May is already Monday 4 May in Lisbon.
    vi.setSystemTime(new Date("2026-05-03T23:30:00Z"));
    const asked = serveSlots();

    renderWithProviders(<ClubBooking club={club} />);

    await screen.findByRole("list", { name: "Horários" });
    expect(asked[0].date).toBe("2026-05-04");
    expect(dateField()).toHaveAttribute("min", "2026-05-04");
  });

  it("shows another pitch's slots and keeps the choice in the URL", async () => {
    const asked = serveSlots();
    renderWithProviders(<ClubBooking club={club} />);
    await screen.findByRole("list", { name: "Horários" });

    await userEvent.selectOptions(pitchField(), tennis);
    await waitFor(() => expect(asked.at(-1)).toEqual({ pitchId: tennis, date: "2026-05-01" }));

    expect(currentPath()).toBe(`/clubs/clube-da-foz?pitch=${tennis}`);
    expect(pitchField()).toHaveValue(tennis);
  });

  it("shows another date's slots and keeps the choice in the URL", async () => {
    const asked = serveSlots();
    renderWithProviders(<ClubBooking club={club} />);
    await screen.findByRole("list", { name: "Horários" });

    fireEvent.change(dateField(), { target: { value: "2026-05-04" } });
    await waitFor(() => expect(asked.at(-1)).toEqual({ pitchId: padel, date: "2026-05-04" }));

    expect(currentPath()).toBe("/clubs/clube-da-foz?date=2026-05-04");
  });

  it("starts from the pitch and date in the URL, which is how a link from the search or a return after login opens", async () => {
    resetNavigation(`/clubs/clube-da-foz?pitch=${tennis}&date=2026-05-10`);
    const asked = serveSlots();

    renderWithProviders(<ClubBooking club={club} />);

    await screen.findByRole("list", { name: "Horários" });
    expect(asked).toEqual([{ pitchId: tennis, date: "2026-05-10" }]);
    expect(pitchField()).toHaveValue(tennis);
    expect(dateField()).toHaveValue("2026-05-10");
  });

  it("does not ask the API for a date outside today..+30 days, and says why", async () => {
    resetNavigation("/clubs/clube-da-foz?date=2026-07-01");
    // No handler: the test fails if a request is made.

    renderWithProviders(<ClubBooking club={club} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Escolhe uma data de hoje até 30 dias à frente.");
    expect(dateField()).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("list", { name: "Horários" })).not.toBeInTheDocument();
  });

  it("says so when the pitch has no slots that day", async () => {
    serveSlots(({ date }) => HttpResponse.json({ date, slots: [] }));

    renderWithProviders(<ClubBooking club={club} />);

    expect(await screen.findByText("Sem horários neste dia.")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Horários" })).not.toBeInTheDocument();
  });

  it("shows a message and retries when the slots cannot be loaded", async () => {
    let fails = true;
    serveSlots(({ date }) => (fails ? problem(500, "internal_error") : HttpResponse.json({ date, slots: [slot("2026-05-01T14:00:00Z", "2026-05-01T15:00:00Z")] })));
    renderWithProviders(<ClubBooking club={club} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado.");
    fails = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("list", { name: "Horários" })).toBeInTheDocument();
  });

  it("tells a visitor a club that has just been hidden is no longer available, in words that fit a visitor", async () => {
    serveSlots(() => problem(404, "club_not_found"));

    renderWithProviders(<ClubBooking club={club} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Este clube já não está disponível.");
    expect(screen.getByRole("alert")).not.toHaveTextContent("teu");
  });

  it("shows nothing when the club has no pitch", () => {
    renderWithProviders(<ClubBooking club={{ ...club, pitches: [] }} />);

    expect(screen.queryByRole("heading", { name: "Horários" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Campo")).not.toBeInTheDocument();
  });
});
