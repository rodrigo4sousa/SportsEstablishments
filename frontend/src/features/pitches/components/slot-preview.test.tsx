import { fireEvent, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { SlotPreview } from "./slot-preview";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const pitchId = "0199f7a0-0000-7000-8000-0000000000d1";
const url = `/api/manage/clubs/${clubId}/pitches/${pitchId}/slots`;

function slots(date: string, minutes: number, instants: string[]) {
  return HttpResponse.json({
    date,
    slotDurationMinutes: minutes,
    slots: instants.slice(0, -1).map((startsAt, index) => ({ startsAt, endsAt: instants[index + 1], price: 20 })),
  });
}

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

const dateField = () => screen.getByLabelText("Data") as HTMLInputElement;
const chosen = (date: string) => fireEvent.change(dateField(), { target: { value: date } });
const texts = () => screen.getAllByRole("listitem").map((item) => (item.textContent ?? "").replace(/\s/g, " "));

describe("SlotPreview", () => {
  beforeEach(() => {
    // 23:30 UTC on 24 October 2026 is already 00:30 on the 25th in Lisbon (WEST).
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-10-24T23:30:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts on today in Lisbon, not the UTC date, and limits the picker to today..+30 days", async () => {
    let asked = "";
    server.use(
      http.get(url, ({ request }) => {
        asked = new URL(request.url).searchParams.get("date") ?? "";
        return slots(asked, 60, ["2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"]);
      }),
    );

    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    expect(dateField()).toHaveValue("2026-10-25");
    expect(dateField()).toHaveAttribute("min", "2026-10-25");
    expect(dateField()).toHaveAttribute("max", "2026-11-24");
    await screen.findByRole("list", { name: "Slots gerados" });
    expect(asked).toBe("2026-10-25");
  });

  it("lists a normal day's slots in Lisbon time (summer: UTC+1)", async () => {
    server.use(
      http.get(url, () =>
        slots("2026-10-27", 90, ["2026-10-27T09:00:00Z", "2026-10-27T10:30:00Z", "2026-10-27T12:00:00Z"]),
      ),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    chosen("2026-10-27");

    await screen.findByRole("list", { name: "Slots gerados" });

    // 27 October is after the clocks went back: Lisbon is on UTC.
    expect(texts()).toEqual(["09:00 – 10:30 · 20,00 €", "10:30 – 12:00 · 20,00 €"]);
    expect(screen.getByText(/2 slots de 90 minutos/)).toBeInTheDocument();
  });

  it("labels the repeated October hour (verão) and (inverno) on the clock-change day", async () => {
    server.use(
      http.get(url, () =>
        slots("2026-10-25", 60, [
          "2026-10-24T23:00:00Z", // 00:00 WEST
          "2026-10-25T00:00:00Z", // 01:00 WEST
          "2026-10-25T01:00:00Z", // 01:00 WET
          "2026-10-25T02:00:00Z", // 02:00 WET
          "2026-10-25T03:00:00Z",
          "2026-10-25T04:00:00Z",
        ]),
      ),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    await screen.findByRole("list", { name: "Slots gerados" });

    expect(texts()).toEqual([
      "00:00 – 01:00 (verão) · 20,00 €",
      "01:00 (verão) – 01:00 (inverno) · 20,00 €",
      "01:00 (inverno) – 02:00 · 20,00 €",
      "02:00 – 03:00 · 20,00 €",
      "03:00 – 04:00 · 20,00 €",
    ]);
    expect(screen.getByText(/5 slots de 60 minutos/)).toBeInTheDocument();
  });

  it("shows Lisbon time whatever the browser time zone is", async () => {
    // The instant is a summer one: 10:00Z is 11:00 in Lisbon, not 10:00 or 06:00.
    server.use(http.get(url, () => slots("2026-10-25", 60, ["2026-10-25T00:00:00Z", "2026-10-25T01:00:00Z"])));
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    await screen.findByRole("list", { name: "Slots gerados" });

    expect(texts()[0]).toContain("01:00 (verão)");
  });

  it("marks the slots that overlap a block as Bloqueado, partial overlaps included, and leaves the others alone", async () => {
    server.use(
      http.get(url, () =>
        HttpResponse.json({
          date: "2026-10-25",
          slotDurationMinutes: 60,
          slots: [
            { startsAt: "2026-10-24T23:00:00Z", endsAt: "2026-10-25T00:00:00Z", blocked: false, price: 20 },
            // The block 01:30 (verão) to 02:30 cuts into these three.
            { startsAt: "2026-10-25T00:00:00Z", endsAt: "2026-10-25T01:00:00Z", blocked: true, price: 20 },
            { startsAt: "2026-10-25T01:00:00Z", endsAt: "2026-10-25T02:00:00Z", blocked: true, price: 20 },
            { startsAt: "2026-10-25T02:00:00Z", endsAt: "2026-10-25T03:00:00Z", blocked: true, price: 20 },
            { startsAt: "2026-10-25T03:00:00Z", endsAt: "2026-10-25T04:00:00Z", blocked: false, price: 20 },
          ],
        }),
      ),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    await screen.findByRole("list", { name: "Slots gerados" });

    const items = screen.getAllByRole("listitem");
    expect(items.map((item) => item.hasAttribute("data-blocked"))).toEqual([false, true, true, true, false]);
    expect(screen.getAllByText("Bloqueado")).toHaveLength(3);
    expect(texts()[1]).toBe("01:00 (verão) – 01:00 (inverno) · 20,00 € Bloqueado");
    expect(texts()[0]).not.toContain("Bloqueado");
  });

  it("shows each slot's price, higher inside a price band than outside it (US10)", async () => {
    server.use(
      http.get(url, () =>
        HttpResponse.json({
          date: "2026-10-27",
          slotDurationMinutes: 60,
          slots: [
            { startsAt: "2026-10-27T17:00:00Z", endsAt: "2026-10-27T18:00:00Z", blocked: false, booked: false, price: 20 },
            { startsAt: "2026-10-27T18:00:00Z", endsAt: "2026-10-27T19:00:00Z", blocked: false, booked: false, price: 32.5 },
            { startsAt: "2026-10-27T19:00:00Z", endsAt: "2026-10-27T20:00:00Z", blocked: false, booked: true, price: 32.5 },
          ],
        }),
      ),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    chosen("2026-10-27");

    await screen.findByRole("list", { name: "Slots gerados" });

    expect(texts()).toEqual(["17:00 – 18:00 · 20,00 €", "18:00 – 19:00 · 32,50 €", "19:00 – 20:00 · 32,50 € Reservado"]);
  });

  it("marks the slots taken by a booking as Reservado, next to Bloqueado when a slot is both", async () => {
    server.use(
      http.get(url, () =>
        HttpResponse.json({
          date: "2026-10-27",
          slotDurationMinutes: 60,
          slots: [
            { startsAt: "2026-10-27T09:00:00Z", endsAt: "2026-10-27T10:00:00Z", blocked: false, booked: false, price: 20 },
            { startsAt: "2026-10-27T10:00:00Z", endsAt: "2026-10-27T11:00:00Z", blocked: false, booked: true, price: 20 },
            { startsAt: "2026-10-27T11:00:00Z", endsAt: "2026-10-27T12:00:00Z", blocked: true, booked: false, price: 20 },
            { startsAt: "2026-10-27T12:00:00Z", endsAt: "2026-10-27T13:00:00Z", blocked: true, booked: true, price: 20 },
          ],
        }),
      ),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    chosen("2026-10-27");

    await screen.findByRole("list", { name: "Slots gerados" });

    // 27 October is after the clocks went back: Lisbon is on UTC.
    expect(texts()).toEqual(["09:00 – 10:00 · 20,00 €", "10:00 – 11:00 · 20,00 € Reservado", "11:00 – 12:00 · 20,00 € Bloqueado", "12:00 – 13:00 · 20,00 € Reservado Bloqueado"]);
    const items = screen.getAllByRole("listitem");
    expect(items.map((item) => item.hasAttribute("data-booked"))).toEqual([false, true, false, true]);
    expect(items.map((item) => item.hasAttribute("data-blocked"))).toEqual([false, false, true, true]);
    expect(screen.getAllByText("Reservado")).toHaveLength(2);
    expect(screen.getAllByText("Bloqueado")).toHaveLength(2);
  });

  it("says that blocks and bookings are marked, no longer that bookings are ignored", async () => {
    server.use(http.get(url, () => slots("2026-10-27", 60, ["2026-10-27T09:00:00Z", "2026-10-27T10:00:00Z"])));
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    chosen("2026-10-27");
    await screen.findByRole("list", { name: "Slots gerados" });

    expect(screen.getByText(/os bloqueios e as reservas aparecem marcados/)).toBeInTheDocument();
    expect(screen.queryByText(/as reservas ainda não são tidas em conta/)).not.toBeInTheDocument();
    expect(screen.queryByText("Reservado")).not.toBeInTheDocument();
  });

  it("shows no Bloqueado marker when nothing is blocked", async () => {
    server.use(http.get(url, () => slots("2026-10-25", 60, ["2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"])));
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    await screen.findByRole("list", { name: "Slots gerados" });

    expect(screen.queryByText("Bloqueado")).not.toBeInTheDocument();
  });

  it("says so when a day has no slots (closed day)", async () => {
    server.use(http.get(url, () => HttpResponse.json({ date: "2026-10-26", slotDurationMinutes: 60, slots: [] })));
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    expect(await screen.findByText(/Sem slots neste dia/)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Slots gerados" })).not.toBeInTheDocument();
  });

  it.each([
    ["2026-10-24", "yesterday in the UTC calendar, but before today in Lisbon"],
    ["2026-11-25", "31 days ahead"],
    ["2025-10-25", "a year ago"],
  ])("refuses %s (%s) without asking the API", async (date) => {
    const asked: string[] = [];
    server.use(
      http.get(url, ({ request }) => {
        asked.push(new URL(request.url).searchParams.get("date") ?? "");
        return slots("2026-10-25", 60, ["2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"]);
      }),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    await screen.findByRole("list", { name: "Slots gerados" });

    chosen(date);

    expect(await screen.findByRole("alert")).toHaveTextContent("Escolhe uma data de hoje até 30 dias à frente.");
    expect(dateField()).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("list", { name: "Slots gerados" })).not.toBeInTheDocument();
    expect(asked).toEqual(["2026-10-25"]);
  });

  it.each(["2026-10-25", "2026-11-24"])("accepts the limit %s", async (date) => {
    server.use(http.get(url, ({ request }) => slots(new URL(request.url).searchParams.get("date")!, 60, ["2026-11-24T09:00:00Z", "2026-11-24T10:00:00Z"])));
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    chosen(date);

    await screen.findByRole("list", { name: "Slots gerados" });

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("asks for a date when the field is cleared", async () => {
    server.use(http.get(url, () => slots("2026-10-25", 60, ["2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"])));
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);
    await screen.findByRole("list", { name: "Slots gerados" });

    chosen("");

    expect(await screen.findByRole("alert")).toHaveTextContent("Escolhe uma data.");
  });

  it("shows the API's message and a way to try again when the preview fails", async () => {
    let calls = 0;
    server.use(
      http.get(url, () => (++calls === 1 ? problem(500, "internal_error") : slots("2026-10-25", 60, ["2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"]))),
    );
    renderWithProviders(<SlotPreview clubId={clubId} pitchId={pitchId} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    fireEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

    await waitFor(() => expect(screen.getByRole("list", { name: "Slots gerados" })).toBeInTheDocument());
  });
});
