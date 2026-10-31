import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { CalendarBlock, CalendarBooking, ClubBooking } from "../types";
import { ClubCalendar } from "./club-calendar";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const CLUB = "0199f7a0-0000-7000-8000-0000000000c1";
const PADEL = "0199f7a0-0000-7000-8000-0000000000d1";
const TENNIS = "0199f7a0-0000-7000-8000-0000000000d2";
const FIRST = "0199f7a0-0000-7000-8000-0000000000b1";
const SECOND = "0199f7a0-0000-7000-8000-0000000000b2";
const THIRD = "0199f7a0-0000-7000-8000-0000000000b3";
const BLOCK = "0199f7a0-0000-7000-9000-0000000000a1";

type PitchSpec = { id: string; name: string; sport?: string; status?: string; hours: [string, string] | null };

const padel: PitchSpec = { id: PADEL, name: "Padel 1", sport: "padel", hours: ["09:00", "13:00"] };
const tennis: PitchSpec = { id: TENNIS, name: "Ténis 1", sport: "tennis", hours: ["10:00", "12:00"] };

/** 10:00-11:00 in Lisbon (WEST) on Monday 4 May 2026. */
const booking = (patch: Partial<CalendarBooking> = {}): CalendarBooking => ({
  id: FIRST,
  pitchId: PADEL,
  startsAt: "2026-05-04T09:00:00Z",
  endsAt: "2026-05-04T10:00:00Z",
  status: "confirmed",
  price: 18.5,
  bookerName: "Rita Jogadora",
  createdByClub: false,
  ...patch,
});

const block = (patch: Partial<CalendarBlock> = {}): CalendarBlock => ({
  id: BLOCK,
  pitchId: TENNIS,
  startsAt: "2026-05-04T10:00:00Z",
  endsAt: "2026-05-04T11:00:00Z",
  reason: "Manutenção da rede",
  ...patch,
});

const details = (patch: Partial<ClubBooking> = {}): ClubBooking => ({
  id: FIRST,
  pitchId: PADEL,
  pitchName: "Padel 1",
  sport: "padel",
  startsAt: "2026-05-04T09:00:00Z",
  endsAt: "2026-05-04T10:00:00Z",
  price: 18.5,
  status: "confirmed",
  bookerName: "Rita Jogadora",
  createdByClub: false,
  customerPhone: null,
  createdAt: "2026-05-01T09:30:00Z",
  cancelledAt: null,
  cancellationReason: null,
  canCancel: true,
  canMarkNoShow: false,
  canUndoNoShow: false,
  playerNoShows: null,
  ...patch,
});

function problem(status: number, code: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...extra }, { status, headers: { "Content-Type": "application/problem+json" } });
}

type World = {
  pitches?: PitchSpec[];
  bookings?: CalendarBooking[];
  blocks?: CalendarBlock[];
};

/** Serves the calendar like the API does: one day, the opening hours of that day, cancelled bookings only when asked for. */
function serveCalendar(world: World = {}) {
  const asked: URLSearchParams[] = [];
  server.use(
    http.get(`/api/manage/clubs/${CLUB}/calendar`, ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.push(query);
      const from = query.get("from") ?? "";
      const includeCancelled = query.get("includeCancelled") === "true";
      return HttpResponse.json({
        from,
        to: query.get("to"),
        pitches: (world.pitches ?? [padel, tennis]).map((pitch) => ({
          id: pitch.id,
          name: pitch.name,
          sport: pitch.sport ?? "padel",
          status: pitch.status ?? "active",
          slotDurationMinutes: 60,
          days: [{ date: from, opens: pitch.hours?.[0] ?? null, closes: pitch.hours?.[1] ?? null }],
        })),
        bookings: (world.bookings ?? []).filter((candidate) => includeCancelled || !candidate.status.startsWith("cancelled")),
        blocks: world.blocks ?? [],
      });
    }),
  );
  return asked;
}

const grid = () => screen.getByRole("group", { name: "Padel 1" }).parentElement as HTMLElement;
const column = (name: string) => screen.getByRole("group", { name });
const hourLabels = () => within(screen.getByRole("list", { name: "Horas" })).getAllByRole("listitem").map((item) => item.textContent);
const topOf = (element: HTMLElement) => Number.parseFloat(element.style.top);

describe("ClubCalendar", () => {
  beforeEach(() => {
    // 10:00 UTC on Monday 4 May 2026 is 11:00 in Lisbon.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-04T10:00:00Z"));
    resetNavigation(`/manage/clubs/${CLUB}/calendar`);
    // The club's name above the calendar (UX-012) is one more request, unrelated to what these tests are about.
    server.use(http.get(`/api/manage/clubs/${CLUB}`, () => HttpResponse.json({ id: CLUB, slug: "clube-teste", name: "Clube Teste", address: "Rua 1", locality: "Porto", phone: "912345678", contactEmail: "geral@clube.pt", cancellationWindowHours: 24, timeZone: "Europe/Lisbon" })));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the daily view", () => {
    it("shows today with a column per pitch, a row per hour and what is in them", async () => {
      const asked = serveCalendar({ bookings: [booking()], blocks: [block()] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("button", { name: /Rita Jogadora/ });
      expect(asked[0].get("from")).toBe("2026-05-04");
      expect(asked[0].get("to")).toBe("2026-05-04");
      expect(asked[0].get("includeCancelled")).toBe("false");
      expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("segunda-feira, 4 de maio (04/05/2026)");
      // From the earliest opening (09:00) to the latest closing (13:00): four rows.
      expect(hourLabels()).toEqual(["09:00", "10:00", "11:00", "12:00"]);
      expect(within(column("Padel 1")).getByText("Padel")).toBeInTheDocument();
      expect(within(column("Ténis 1")).getByText("Ténis")).toBeInTheDocument();
      expect(grid()).toBeInTheDocument();
    });

    it("shows a booking with the player's name, the time in Lisbon and the status, in the pitch's column", async () => {
      serveCalendar({ bookings: [booking()] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const button = await screen.findByRole("button", { name: "Rita Jogadora 10:00 – 11:00 Confirmada" });
      expect(within(column("Padel 1")).getByRole("button", { name: /Rita Jogadora/ })).toBe(button);
      expect(within(column("Ténis 1")).queryByRole("button", { name: /Rita Jogadora/ })).not.toBeInTheDocument();
      expect(button).toHaveTextContent("Rita Jogadora");
      expect(button).toHaveTextContent("10:00 – 11:00");
      expect(button).toHaveTextContent("Confirmada");
    });

    it("positions a booking by real elapsed time: one hour below the top of the grid, one hour tall", async () => {
      serveCalendar({ bookings: [booking()] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const button = await screen.findByRole("button", { name: /Rita Jogadora/ });
      // The grid starts at 09:00, the booking at 10:00: 64 px down, 64 px tall.
      expect(topOf(button)).toBe(64);
      expect(Number.parseFloat(button.style.height)).toBe(64);
      expect(button).toHaveAttribute("data-minute-start", "600");
    });

    it("shows a block in a different style, with its reason and time", async () => {
      serveCalendar({ blocks: [block()] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const box = await screen.findByRole("group", { name: "Bloqueio, 11:00 – 12:00, Manutenção da rede" });
      expect(within(column("Ténis 1")).getByRole("group", { name: /Bloqueio/ })).toBe(box);
      expect(box).toHaveTextContent("Bloqueio");
      expect(box).toHaveTextContent("Manutenção da rede");
      expect(box).toHaveAttribute("data-kind", "block");
      expect(box.tagName).not.toBe("BUTTON"); // not something to open
      expect(box.className).toContain("border-dashed");
      expect(topOf(box)).toBe(128); // 11:00 on a grid that starts at 09:00
    });

    it("shows a block without a reason with just its time", async () => {
      serveCalendar({ blocks: [block({ reason: null })] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByRole("group", { name: "Bloqueio, 11:00 – 12:00" })).toHaveTextContent("Bloqueio");
    });

    it("styles each status differently and says it in words", async () => {
      serveCalendar({
        bookings: [
          booking({ id: FIRST, startsAt: "2026-05-04T08:00:00Z", endsAt: "2026-05-04T09:00:00Z" }),
          booking({ id: SECOND, startsAt: "2026-05-04T09:00:00Z", endsAt: "2026-05-04T10:00:00Z", status: "no_show", bookerName: "Bruno Jogador" }),
        ],
      });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const confirmed = await screen.findByRole("button", { name: /Rita Jogadora/ });
      const noShow = screen.getByRole("button", { name: "Bruno Jogador 10:00 – 11:00 Falta" });
      expect(confirmed.className).toContain("emerald");
      expect(noShow.className).toContain("amber");
      expect(noShow).toHaveTextContent("Falta");
    });

    it("marks a pitch that is closed that day, and shades what is outside its opening hours", async () => {
      serveCalendar({ pitches: [padel, { ...tennis, hours: null }] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Ténis 1" });
      expect(within(column("Ténis 1")).getByText("Fechado")).toBeInTheDocument();
      expect(within(column("Padel 1")).queryByText("Fechado")).not.toBeInTheDocument();
      // Only Padel counts for the grid: 09:00 to 13:00.
      expect(hourLabels()).toEqual(["09:00", "10:00", "11:00", "12:00"]);
      const open = column("Padel 1").querySelector("[data-open-range]") as HTMLElement;
      expect(topOf(open)).toBe(0);
      expect(Number.parseFloat(open.style.height)).toBe(256);
    });

    it("shows an inactive pitch as such", async () => {
      serveCalendar({ pitches: [padel, { ...tennis, status: "inactive" }] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByText("Ténis · Inativo")).toBeInTheDocument();
    });

    it("puts overlapping bookings side by side", async () => {
      serveCalendar({
        bookings: [
          booking({ id: FIRST, status: "cancelled_by_club", bookerName: "Ana Cancelada" }),
          booking({ id: SECOND, bookerName: "Bruno Jogador" }),
        ],
      });
      resetNavigation(`/manage/clubs/${CLUB}/calendar?cancelled=1`);

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const cancelled = await screen.findByRole("button", { name: /Ana Cancelada/ });
      const rebooked = screen.getByRole("button", { name: /Bruno Jogador/ });
      expect(cancelled.style.width).toBe("calc(50% - 4px)");
      expect(rebooked.style.width).toBe("calc(50% - 4px)");
      expect(new Set([cancelled.style.left, rebooked.style.left]).size).toBe(2);
    });

    it("says when the club has no pitches", async () => {
      serveCalendar({ pitches: [] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByText("Este clube ainda não tem campos ativos.")).toBeInTheDocument();
    });

    it("says when nothing is open that day and there is nothing to show", async () => {
      serveCalendar({ pitches: [{ ...padel, hours: null }] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByText("Nenhum campo abre neste dia e não há reservas nem bloqueios.")).toBeInTheDocument();
    });

    it("shows an error with a way to try again", async () => {
      let failing = true;
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/calendar`, () => (failing ? problem(500, "internal_error") : HttpResponse.json({ from: "2026-05-04", to: "2026-05-04", pitches: [], bookings: [], blocks: [] }))));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      failing = false;
      await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

      expect(await screen.findByText("Este clube ainda não tem campos ativos.")).toBeInTheDocument();
    });

    it("shows the not-found page for a club the API answers 404 for (unknown, or another owner's)", async () => {
      server.use(http.get(`/api/manage/clubs/${CLUB}/calendar`, () => problem(404, "club_not_found")));

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByRole("heading", { name: "Clube não encontrado" })).toBeInTheDocument();
      expect(screen.getByText("Este clube não existe ou não é teu.")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Hoje" })).not.toBeInTheDocument();
    });

    it("links back to the club", async () => {
      serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByRole("link", { name: "← Voltar ao clube" })).toHaveAttribute("href", `/manage/clubs/${CLUB}`);
    });
  });

  describe("the two days the clocks change", () => {
    it("lays out 2026-10-25 (25 hours): 25 rows, both 01:00 hours labelled, and the two 01:30 bookings an hour apart", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2026-10-25`);
      serveCalendar({
        pitches: [{ ...padel, hours: ["00:00", "00:00"] }],
        bookings: [
          booking({ id: FIRST, startsAt: "2026-10-25T00:30:00Z", endsAt: "2026-10-25T01:00:00Z", bookerName: "Primeira" }), // 01:30 WEST
          booking({ id: SECOND, startsAt: "2026-10-25T01:30:00Z", endsAt: "2026-10-25T02:00:00Z", bookerName: "Segunda" }), // 01:30 WET
          booking({ id: THIRD, startsAt: "2026-10-25T09:00:00Z", endsAt: "2026-10-25T10:00:00Z", bookerName: "Manhã" }), // 09:00 WET
        ],
      });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const first = await screen.findByRole("button", { name: /Primeira/ });
      const second = screen.getByRole("button", { name: /Segunda/ });
      const morning = screen.getByRole("button", { name: /Manhã/ });
      const labels = hourLabels();
      expect(labels).toHaveLength(25);
      expect(labels.slice(0, 5)).toEqual(["00:00", "01:00 (verão)", "01:00 (inverno)", "02:00", "03:00"]);
      expect(labels.at(-1)).toBe("23:00");
      // Real elapsed time: 90, 150 and 600 minutes from the start of the day, at 64 px an hour.
      expect(topOf(first)).toBeCloseTo(96);
      expect(topOf(second)).toBeCloseTo(160);
      expect(topOf(morning)).toBeCloseTo(640);
      expect(first).toHaveAccessibleName("Primeira 01:30 (verão) – 01:00 (inverno) Confirmada");
      expect(second).toHaveAccessibleName("Segunda 01:30 (inverno) – 02:00 Confirmada");
      expect(morning).toHaveAccessibleName("Manhã 09:00 – 10:00 Confirmada");
      expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("domingo, 25 de outubro (25/10/2026)");
    });

    it("has the last hour of 2026-10-25 and nothing from the next day", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2026-10-25`);
      serveCalendar({
        pitches: [{ ...padel, hours: ["00:00", "00:00"] }],
        bookings: [booking({ id: FIRST, startsAt: "2026-10-25T23:00:00Z", endsAt: "2026-10-26T00:00:00Z", bookerName: "Última" })],
      });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const last = await screen.findByRole("button", { name: /Última/ });
      expect(Number.parseFloat(last.style.top) + Number.parseFloat(last.style.height)).toBeCloseTo(25 * 64);
    });

    it("lays out 2026-03-29 (23 hours): 23 rows, no 01:00, and a booking at 02:00 on the second row", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2026-03-29`);
      serveCalendar({
        pitches: [{ ...padel, hours: ["00:00", "00:00"] }],
        bookings: [
          booking({ id: FIRST, startsAt: "2026-03-29T01:00:00Z", endsAt: "2026-03-29T02:00:00Z", bookerName: "Cedo" }), // 02:00 WEST
          booking({ id: SECOND, startsAt: "2026-03-29T09:00:00Z", endsAt: "2026-03-29T10:00:00Z", bookerName: "Manhã" }), // 10:00 WEST
        ],
      });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const early = await screen.findByRole("button", { name: /Cedo/ });
      const morning = screen.getByRole("button", { name: /Manhã/ });
      const labels = hourLabels();
      expect(labels).toHaveLength(23);
      expect(labels.slice(0, 4)).toEqual(["00:00", "02:00", "03:00", "04:00"]);
      expect(labels).not.toContain("01:00");
      expect(labels.at(-1)).toBe("23:00");
      expect(topOf(early)).toBeCloseTo(64); // the second row
      expect(topOf(morning)).toBeCloseTo(9 * 64);
      expect(early).toHaveAccessibleName("Cedo 02:00 – 03:00 Confirmada");
      expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("domingo, 29 de março (29/03/2026)");
    });

    it("lays out the opening hours of a clock-change day by elapsed time too", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2026-10-25`);
      serveCalendar({ pitches: [{ ...padel, hours: ["09:00", "13:00"] }] });

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Padel 1" });
      // 09:00 on the clock is the tenth hour of this day: the grid starts there and has four rows.
      expect(hourLabels()).toEqual(["09:00", "10:00", "11:00", "12:00"]);
      const open = column("Padel 1").querySelector("[data-open-range]") as HTMLElement;
      expect(topOf(open)).toBe(0);
      expect(Number.parseFloat(open.style.height)).toBe(4 * 64);
    });
  });

  describe("navigating", () => {
    it("goes to the day before and the day after, in the address and in the request", async () => {
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      await userEvent.click(screen.getByRole("button", { name: "Dia seguinte" }));

      await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-05-05"));
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?date=2026-05-05`);
      expect(await screen.findByRole("heading", { level: 2, name: /terça-feira, 5 de maio/ })).toBeInTheDocument();

      await userEvent.click(screen.getByRole("button", { name: "Dia anterior" }));
      await userEvent.click(screen.getByRole("button", { name: "Dia anterior" }));

      await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-05-03"));
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?date=2026-05-03`);
      expect(await screen.findByRole("heading", { level: 2, name: /domingo, 3 de maio/ })).toBeInTheDocument();
    });

    it("goes back to today with «Hoje», and today is the plain address", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2026-10-25&cancelled=1`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      await userEvent.click(screen.getByRole("button", { name: "Hoje" }));

      await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-05-04"));
      // The toggle is not part of «Hoje»: only the day changes.
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?cancelled=1`);
      expect(await screen.findByRole("heading", { level: 2, name: /segunda-feira, 4 de maio/ })).toBeInTheDocument();
    });

    it("uses the Lisbon date for «Hoje», not the browser's: just after midnight in Lisbon it is already tomorrow", async () => {
      // 23:30 UTC on 4 May is 00:30 on 5 May in Lisbon.
      vi.setSystemTime(new Date("2026-05-04T23:30:00Z"));
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Padel 1" });

      expect(asked[0].get("from")).toBe("2026-05-05");
    });

    it("goes to the date typed in the date field", async () => {
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-10-25" } });

      await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-10-25"));
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?date=2026-10-25`);
    });

    it("opens on the day the address says, and on today for a wrong date", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2026-02-30`);
      const asked = serveCalendar();

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Padel 1" });
      expect(asked[0].get("from")).toBe("2026-05-04");
    });

    it("can look at the past and at days far ahead: there is no booking window for the owner", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?date=2024-03-04`);
      const asked = serveCalendar();

      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Padel 1" });
      expect(asked[0].get("from")).toBe("2024-03-04");
    });

    it("closes the open booking when the day changes", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("region", { name: "Detalhes da reserva" });

      await userEvent.click(screen.getByRole("button", { name: "Dia seguinte" }));

      await waitFor(() => expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument());
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?date=2026-05-05`);
    });
  });

  describe("cancelled bookings", () => {
    it("hides them by default and shows them, marked, once asked for", async () => {
      const asked = serveCalendar({
        bookings: [
          booking({ id: FIRST }),
          booking({ id: SECOND, status: "cancelled_by_player", bookerName: "Ana Cancelada", startsAt: "2026-05-04T10:00:00Z", endsAt: "2026-05-04T11:00:00Z" }),
          booking({ id: THIRD, status: "cancelled_by_club", bookerName: "Rui Cancelado", startsAt: "2026-05-04T11:00:00Z", endsAt: "2026-05-04T12:00:00Z" }),
        ],
      });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("button", { name: /Rita Jogadora/ });
      expect(screen.getByLabelText("Mostrar reservas canceladas")).not.toBeChecked();
      expect(screen.queryByRole("button", { name: /Ana Cancelada/ })).not.toBeInTheDocument();
      expect(asked.at(-1)?.get("includeCancelled")).toBe("false");

      await userEvent.click(screen.getByLabelText("Mostrar reservas canceladas"));

      expect(await screen.findByRole("button", { name: "Ana Cancelada 11:00 – 12:00 Cancelada pelo jogador" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Rui Cancelado 12:00 – 13:00 Cancelada pelo clube" })).toBeInTheDocument();
      expect(asked.at(-1)?.get("includeCancelled")).toBe("true");
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?cancelled=1`);
      expect(screen.getByRole("button", { name: /Ana Cancelada/ }).className).toContain("line-through");

      await userEvent.click(screen.getByLabelText("Mostrar reservas canceladas"));

      await waitFor(() => expect(screen.queryByRole("button", { name: /Ana Cancelada/ })).not.toBeInTheDocument());
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar`);
    });

    it("opens with the toggle on when the address says so, and keeps it when the day changes", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?cancelled=1`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });
      expect(screen.getByLabelText("Mostrar reservas canceladas")).toBeChecked();
      expect(asked[0].get("includeCancelled")).toBe("true");

      await userEvent.click(screen.getByRole("button", { name: "Dia seguinte" }));

      await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-05-05"));
      expect(asked.at(-1)?.get("includeCancelled")).toBe("true");
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?date=2026-05-05&cancelled=1`);
    });
  });

  describe("opening a booking", () => {
    it("shows its details: the player's name, pitch, date, time, price, status and when it was made", async () => {
      serveCalendar({ bookings: [booking()] });
      const fetched: string[] = [];
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => {
          fetched.push(FIRST);
          return HttpResponse.json(details());
        }),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await userEvent.click(await screen.findByRole("button", { name: /Rita Jogadora/ }));

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await within(panel).findByText("Rita Jogadora");
      expect(fetched).toEqual([FIRST]);
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      const lines = Object.fromEntries([...panel.querySelectorAll("dt")].map((term) => [term.textContent, (term.nextElementSibling?.textContent ?? "").replace(/\s/g, " ")]));
      expect(lines).toEqual({
        Jogador: "Rita Jogadora",
        Campo: "Padel 1 · Padel",
        Data: "segunda-feira, 4 de maio (04/05/2026)",
        Hora: "10:00 – 11:00",
        Preço: "18,50 €",
        Estado: "Confirmada",
        "Reservada em": "01/05/2026 10:30",
      });
      expect(screen.getByRole("button", { name: /Rita Jogadora/ })).toHaveAttribute("aria-pressed", "true");
      expect(within(panel).getByRole("button", { name: "Cancelar reserva" })).toBeInTheDocument();
    });

    it("moves focus to the details when a booking is opened, so they are in view wherever the grid left them (UX-002, UX-004)", async () => {
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await userEvent.click(await screen.findByRole("button", { name: /Rita Jogadora/ }));

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      expect(within(panel).getByRole("heading", { name: "Reserva" })).toHaveFocus();
    });

    it("opens the booking the address names and closes it with «Fechar»", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await within(panel).findByText("Rita Jogadora");
      await userEvent.click(within(panel).getByRole("button", { name: "Fechar" }));

      expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument();
      expect(currentPath()).toBe(`/manage/clubs/${CLUB}/calendar`);
    });

    it("says so when the booking is not there (any more), with no way to cancel it", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      serveCalendar();
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => problem(404, "booking_not_found")));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });

      expect(await within(panel).findByRole("alert")).toHaveTextContent("Esta reserva já não existe.");
      expect(within(panel).queryByRole("button", { name: "Cancelar reserva" })).not.toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Tentar novamente" })).not.toBeInTheDocument();
    });

    it("ignores a booking id in the address that is not a GUID, without asking the API", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=nao-e-um-guid`);
      serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Padel 1" });

      expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument();
    });

    it("shows when and why a booking was cancelled, and offers no cancel button", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      serveCalendar();
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () =>
          HttpResponse.json(details({ status: "cancelled_by_club", canCancel: false, cancelledAt: "2026-05-02T14:00:00Z", cancellationReason: "Campo em obras" })),
        ),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });

      expect(await within(panel).findByText("Cancelada pelo clube")).toBeInTheDocument();
      expect(within(panel).getByText("02/05/2026 15:00")).toBeInTheDocument();
      expect(within(panel).getByText("Campo em obras")).toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Cancelar reserva" })).not.toBeInTheDocument();
    });

    it("follows the API's canCancel: a confirmed booking that has started has no button, and says why", async () => {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      serveCalendar();
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details({ canCancel: false }))));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });

      expect(await within(panel).findByText("Esta reserva já começou, por isso já não pode ser cancelada.")).toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Cancelar reserva" })).not.toBeInTheDocument();
    });
  });

  describe("cancelling a booking (US12)", () => {
    async function openBooking(world: World = { bookings: [booking()] }) {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      const asked = serveCalendar(world);
      const view = renderWithProviders(<ClubCalendar clubId={CLUB} />);
      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await userEvent.click(await within(panel).findByRole("button", { name: "Cancelar reserva" }));
      return { asked, panel, ...view };
    }

    const reasonField = () => screen.getByLabelText("Motivo do cancelamento");

    it("asks for a reason first, and keeping the booking sends nothing", async () => {
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      let posted = 0;
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, () => {
        posted += 1;
        return HttpResponse.json(details());
      }));
      const { panel } = await openBooking();

      expect(within(panel).getByRole("form", { name: "Cancelar reserva" })).toBeInTheDocument();
      expect(reasonField()).toBeInTheDocument();
      await userEvent.click(within(panel).getByRole("button", { name: "Manter reserva" }));

      expect(within(panel).queryByRole("form", { name: "Cancelar reserva" })).not.toBeInTheDocument();
      expect(within(panel).getByRole("button", { name: "Cancelar reserva" })).toBeInTheDocument();
      expect(posted).toBe(0);
    });

    it("opens the form with focus on the reason, and keeping the booking puts focus back on the button (UX-002)", async () => {
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      const { panel } = await openBooking();

      expect(reasonField()).toHaveFocus();

      await userEvent.click(within(panel).getByRole("button", { name: "Manter reserva" }));

      expect(within(panel).getByRole("button", { name: "Cancelar reserva" })).toHaveFocus();
    });

    it("shows a toast, and no message in the details, when the server fails (UX-019)", async () => {
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())),
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, () => problem(500, "internal_error")),
      );
      const { panel } = await openBooking();
      await userEvent.type(reasonField(), "Avaria da rede");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
      expect(panel.querySelector("[data-kind]")).toBeNull();
      // The form stays, with what the owner typed, so they can try again.
      expect(reasonField()).toHaveValue("Avaria da rede");
    });

    it.each(["", "   "])("blocks an empty reason («%s») without calling the API", async (text) => {
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      let posted = 0;
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, () => {
        posted += 1;
        return HttpResponse.json(details());
      }));
      await openBooking();
      if (text) await userEvent.type(reasonField(), text);

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await screen.findByText("Indica o motivo do cancelamento.")).toBeInTheDocument();
      expect(reasonField()).toHaveAttribute("aria-invalid", "true");
      expect(posted).toBe(0);
    });

    it("blocks a reason of more than 200 characters and accepts exactly 200", async () => {
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      const bodies: unknown[] = [];
      server.use(
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, async ({ request }) => {
          bodies.push(await request.json());
          return HttpResponse.json(details({ status: "cancelled_by_club", canCancel: false, cancellationReason: "x" }));
        }),
      );
      await openBooking();

      await userEvent.click(reasonField());
      await userEvent.paste("a".repeat(201));
      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await screen.findByText("O motivo pode ter no máximo 200 caracteres.")).toBeInTheDocument();
      expect(bodies).toEqual([]);

      await userEvent.clear(reasonField());
      await userEvent.click(reasonField());
      await userEvent.paste("b".repeat(200));
      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect((bodies[0] as { reason: string }).reason).toHaveLength(200);
    });

    it("cancels with the reason (trimmed), says so, shows the booking as cancelled and loads the calendar again", async () => {
      let isCancelled = false;
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () =>
          HttpResponse.json(isCancelled ? details({ status: "cancelled_by_club", canCancel: false, cancelledAt: "2026-05-04T10:00:00Z", cancellationReason: "Campo em obras" }) : details()),
        ),
      );
      const bodies: unknown[] = [];
      server.use(
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, async ({ request }) => {
          bodies.push(await request.json());
          isCancelled = true;
          return HttpResponse.json(details({ status: "cancelled_by_club", canCancel: false, cancelledAt: "2026-05-04T10:00:00Z", cancellationReason: "Campo em obras" }));
        }),
      );
      const { asked, panel, queryClient } = await openBooking({ bookings: [booking()] });
      const invalidate = vi.spyOn(queryClient, "invalidateQueries");
      const askedBefore = asked.length;
      await userEvent.type(reasonField(), "  Campo em obras  ");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      // The result takes focus where the form was (UX-002), and is not also a live region.
      const notice = await within(panel).findByText("Reserva cancelada. O horário voltou a ficar livre.");
      expect(notice).toHaveFocus();
      expect(notice).not.toHaveAttribute("role");
      expect(bodies).toEqual([{ reason: "Campo em obras" }]);
      expect(await within(panel).findByText("Cancelada pelo clube")).toBeInTheDocument();
      expect(within(panel).getByText("Campo em obras")).toBeInTheDocument();
      expect(within(panel).queryByRole("form", { name: "Cancelar reserva" })).not.toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Cancelar reserva" })).not.toBeInTheDocument();
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["calendar", CLUB], refetchType: "all" });
      // The calendar is read again, and the cancelled booking leaves it (cancelled ones are hidden by default).
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
    });

    it("cannot be sent twice while it is being cancelled", async () => {
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      let release: (() => void) | undefined;
      let requests = 0;
      server.use(
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, async () => {
          requests += 1;
          await new Promise<void>((resolve) => {
            release = resolve;
          });
          return HttpResponse.json(details({ status: "cancelled_by_club", canCancel: false, cancellationReason: "Avaria" }));
        }),
      );
      await openBooking();
      await userEvent.type(reasonField(), "Avaria");
      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      const working = await screen.findByRole("button", { name: "A cancelar…" });

      expect(working).toBeDisabled();
      expect(screen.getByRole("button", { name: "Manter reserva" })).toBeDisabled();
      await userEvent.click(working);
      expect(requests).toBe(1);
      release?.();
      await screen.findByText("Reserva cancelada. O horário voltou a ficar livre.");
    });

    it.each([
      ["booking_not_confirmed", 422, "Esta reserva já não está confirmada, por isso não pode ser cancelada."],
      ["booking_already_started", 422, "Esta reserva já começou, por isso já não pode ser cancelada."],
      ["booking_not_found", 404, "Esta reserva já não existe."],
    ])("shows the message for %s and loads the booking and the calendar again", async (code, status, message) => {
      let gets = 0;
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => {
          gets += 1;
          // After the refusal the booking is what the API says now.
          return gets === 1 ? HttpResponse.json(details()) : HttpResponse.json(details({ status: "cancelled_by_player", canCancel: false }));
        }),
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, () => problem(status, code)),
      );
      const { asked, panel } = await openBooking();
      const askedBefore = asked.length;
      await userEvent.type(reasonField(), "Avaria da rede");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await within(panel).findByText(message)).toHaveFocus();
      // The form is gone, and the message stays even though the reloaded booking no longer offers it.
      await waitFor(() => expect(gets).toBeGreaterThan(1));
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
      expect(within(panel).queryByRole("form", { name: "Cancelar reserva" })).not.toBeInTheDocument();
      expect(within(panel).getByText(message)).toBeInTheDocument();
    });

    it("puts the API's validation message on the reason field", async () => {
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())),
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, () => problem(400, "validation_failed", { errors: { reason: ["Indica o motivo do cancelamento."] } })),
      );
      await openBooking();
      await userEvent.type(reasonField(), "Avaria");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await screen.findByText("Indica o motivo do cancelamento.")).toBeInTheDocument();
      expect(reasonField()).toHaveAttribute("aria-invalid", "true");
      expect(screen.getByRole("form", { name: "Cancelar reserva" })).toBeInTheDocument();
    });

    it("shows a toast for a failure that is not the API's and keeps the form", async () => {
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())),
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, () => problem(500, "internal_error")),
      );
      await openBooking();
      await userEvent.type(reasonField(), "Avaria");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
      expect(screen.getByRole("form", { name: "Cancelar reserva" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Confirmar cancelamento" })).toBeEnabled();
    });
  });

  describe("no-shows (US15)", () => {
    const ENDED = { canCancel: false, canMarkNoShow: true, canUndoNoShow: false } as const;
    const NO_SHOW = { status: "no_show", canCancel: false, canMarkNoShow: false, canUndoNoShow: true } as const;

    /** Opens the booking with the details the API gives; the calendar is the same as in the other tests. */
    async function openDetails(first: Partial<ClubBooking>, then?: { action: "no-show" | "no-show/undo"; details: Partial<ClubBooking> }) {
      resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
      const asked = serveCalendar({ bookings: [booking()] });
      let gets = 0;
      let current = details(first);
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => {
          gets += 1;
          return HttpResponse.json(current);
        }),
      );
      if (then) {
        // The API changes the booking, so loading it again afterwards gives the new state.
        server.use(
          http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/${then.action}`, () => {
            current = details(then.details);
            return HttpResponse.json(current);
          }),
        );
      }

      const view = renderWithProviders(<ClubCalendar clubId={CLUB} />);
      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await within(panel).findByText("Rita Jogadora");
      return { asked, panel, gets: () => gets, ...view };
    }

    it("offers «Marcar falta», and only that, for a confirmed booking that has ended", async () => {
      const { panel } = await openDetails(ENDED);

      expect(within(panel).getByRole("button", { name: "Marcar falta" })).toBeEnabled();
      expect(within(panel).queryByRole("button", { name: "Anular falta" })).not.toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Cancelar reserva" })).not.toBeInTheDocument();
      // Not the «has started» note: it has ended.
      expect(within(panel).queryByText(/já começou/)).not.toBeInTheDocument();
    });

    it("offers «Anular falta», and only that, for a no-show", async () => {
      const { panel } = await openDetails(NO_SHOW);

      expect(within(panel).getByRole("button", { name: "Anular falta" })).toBeEnabled();
      expect(within(panel).queryByRole("button", { name: "Marcar falta" })).not.toBeInTheDocument();
      expect(within(panel).getByText("Falta")).toBeInTheDocument();
    });

    it.each([
      ["a confirmed booking that has not started", { canCancel: true, canMarkNoShow: false, canUndoNoShow: false }],
      ["a booking in progress", { canCancel: false, canMarkNoShow: false, canUndoNoShow: false }],
      ["a booking cancelled by the club", { status: "cancelled_by_club", canCancel: false, canMarkNoShow: false, canUndoNoShow: false }],
      ["a booking cancelled by the player", { status: "cancelled_by_player", canCancel: false, canMarkNoShow: false, canUndoNoShow: false }],
    ] as const)("offers neither button for %s", async (_, patch) => {
      const { panel } = await openDetails(patch);

      expect(within(panel).queryByRole("button", { name: "Marcar falta" })).not.toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Anular falta" })).not.toBeInTheDocument();
    });

    it.each([
      [0, "Sem faltas nos teus clubes"],
      [1, "1 falta nos teus clubes"],
      [3, "3 faltas nos teus clubes"],
    ])("shows the player's number of no-shows at the owner's clubs (%i)", async (count, text) => {
      const { panel } = await openDetails({ ...ENDED, playerNoShows: count });

      expect(within(panel).getByText("Faltas")).toBeInTheDocument();
      expect(within(panel).getByText(text)).toBeInTheDocument();
    });

    it("shows no count for a customer's booking, which is not counted per person", async () => {
      const { panel } = await openDetails({ ...ENDED, createdByClub: true, customerPhone: "912345678", playerNoShows: null });

      expect(within(panel).queryByText("Faltas")).not.toBeInTheDocument();
      expect(within(panel).getByRole("button", { name: "Marcar falta" })).toBeInTheDocument();
    });

    it("marks the no-show, says so, shows the new state and count, and loads the calendar again", async () => {
      const { asked, panel, queryClient } = await openDetails({ ...ENDED, playerNoShows: 1 }, { action: "no-show", details: { ...NO_SHOW, playerNoShows: 2 } });
      const invalidate = vi.spyOn(queryClient, "invalidateQueries");
      const askedBefore = asked.length;

      await userEvent.click(within(panel).getByRole("button", { name: "Marcar falta" }));

      expect(await within(panel).findByText("Falta marcada.")).toHaveFocus();
      expect(within(panel).getByRole("button", { name: "Anular falta" })).toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Marcar falta" })).not.toBeInTheDocument();
      expect(within(panel).getByText("2 faltas nos teus clubes")).toBeInTheDocument();
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["calendar", CLUB], refetchType: "all" });
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
    });

    it("takes the no-show back, says so, and shows the booking confirmed again with the count one lower", async () => {
      const { asked, panel } = await openDetails({ ...NO_SHOW, playerNoShows: 2 }, { action: "no-show/undo", details: { ...ENDED, playerNoShows: 1 } });
      const askedBefore = asked.length;

      await userEvent.click(within(panel).getByRole("button", { name: "Anular falta" }));

      expect(await within(panel).findByText("Falta anulada. A reserva voltou a estar confirmada.")).toBeInTheDocument();
      expect(within(panel).getByRole("button", { name: "Marcar falta" })).toBeInTheDocument();
      expect(within(panel).queryByRole("button", { name: "Anular falta" })).not.toBeInTheDocument();
      expect(within(panel).getByText("1 falta nos teus clubes")).toBeInTheDocument();
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
    });

    it.each([
      ["booking_not_ended", 422, "Só é possível marcar falta depois de a reserva terminar."],
      ["booking_not_confirmed", 422, "Esta reserva já não está confirmada, por isso não pode ser cancelada."],
      ["booking_not_found", 404, "Esta reserva já não existe."],
    ])("shows the message for %s when marking, and loads the calendar again", async (code, status, message) => {
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/no-show`, () => problem(status, code)));
      const { asked, panel } = await openDetails(ENDED);
      const askedBefore = asked.length;

      await userEvent.click(within(panel).getByRole("button", { name: "Marcar falta" }));

      expect((await screen.findAllByText(message)).length).toBeGreaterThan(0);
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
    });

    it("tells that the slot is taken when taking a no-show back is refused with 409, and keeps the no-show", async () => {
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/no-show/undo`, () => problem(409, "slot_taken")));
      const { panel } = await openDetails(NO_SHOW);

      await userEvent.click(within(panel).getByRole("button", { name: "Anular falta" }));

      expect(await within(panel).findByText("Não foi possível anular a falta: outra reserva confirmada já ocupa este horário.")).toHaveFocus();
      expect(within(panel).getByRole("button", { name: "Anular falta" })).toBeEnabled();
      expect(within(panel).getByText("Falta")).toBeInTheDocument();
    });

    it("says the booking is not a no-show (422) when taking it back", async () => {
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/no-show/undo`, () => problem(422, "booking_not_no_show")));
      const { panel } = await openDetails(NO_SHOW);

      await userEvent.click(within(panel).getByRole("button", { name: "Anular falta" }));

      expect(await within(panel).findByText("Esta reserva não está marcada como falta.")).toBeInTheDocument();
    });

    it("shows a toast for any other failure and keeps the button", async () => {
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/no-show`, () => problem(500, "internal_error")));
      const { panel } = await openDetails(ENDED);

      await userEvent.click(within(panel).getByRole("button", { name: "Marcar falta" }));

      expect((await screen.findAllByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).length).toBeGreaterThan(0);
      expect(within(panel).getByRole("button", { name: "Marcar falta" })).toBeEnabled();
    });
  });
});

describe("ClubCalendar on a phone and the open booking (UX-004, UX-005, UX-016)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-04T10:00:00Z"));
    resetNavigation(`/manage/clubs/${CLUB}/calendar`);
    server.use(http.get(`/api/manage/clubs/${CLUB}`, () => HttpResponse.json({ id: CLUB, slug: "clube-teste", name: "Clube Teste", address: "Rua 1", locality: "Porto", phone: "912345678", contactEmail: "geral@clube.pt", cancellationWindowHours: 24, timeZone: "Europe/Lisbon" })));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("names the club above the calendar (UX-012)", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    expect(await screen.findByText("Clube Teste")).toBeInTheDocument();
    // The tab says which club too (UX-012).
    await waitFor(() => expect(document.title).toBe("Calendário · Clube Teste · Reservas de Campos"));
  });

  it("shows one pitch on a phone, the first by default, and every pitch from sm up (UX-005)", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    await screen.findByRole("group", { name: "Padel 1" });
    expect(column("Padel 1")).not.toHaveClass("hidden");
    expect(column("Padel 1")).toHaveClass("sm:block");
    expect(column("Ténis 1")).toHaveClass("hidden", "sm:block");
  });

  it("lets the phone choose the pitch with the «Campo» list, kept in the address", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);
    await screen.findByRole("group", { name: "Padel 1" });

    await userEvent.selectOptions(screen.getByLabelText("Campo"), screen.getByRole("option", { name: /Ténis 1/ }));

    expect(currentPath()).toContain(`pitch=${tennis.id}`);
    await waitFor(() => expect(column("Padel 1")).toHaveClass("hidden"));
    expect(column("Ténis 1")).not.toHaveClass("hidden");
  });

  it("keeps the hours column in view while the pitches scroll sideways (UX-005)", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    const hours = await screen.findByRole("list", { name: "Horas" });

    expect(hours.parentElement).toHaveClass("sticky", "left-0", "bg-background");
  });

  it("lets the pitch columns share the width from sm up, with a minimum, so the booking text is not cut (UX-005)", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    await screen.findByRole("group", { name: "Padel 1" });

    expect(column("Padel 1")).toHaveClass("sm:min-w-44", "sm:flex-1");
  });

  it("makes the scrolling grid reachable with the keyboard and names it (UX-016, scrollable-region-focusable)", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    const region = await screen.findByRole("region", { name: "Calendário do dia" });
    expect(region).toHaveAttribute("tabindex", "0");
  });

  it("names a booking by what it shows, so the visible text is part of its name (UX-016)", async () => {
    serveCalendar({ bookings: [booking()] });
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    const box = await screen.findByRole("button", { name: /Rita Jogadora/ });
    expect(box).not.toHaveAttribute("aria-label");
    expect(box).toHaveAccessibleName("Rita Jogadora 10:00 – 11:00 Confirmada");
  });

  it("puts the details beside the grid on a wide screen and first on a phone (UX-004)", async () => {
    resetNavigation(`/manage/clubs/${CLUB}/calendar?booking=${FIRST}`);
    serveCalendar({ bookings: [booking()] });
    server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
    renderWithProviders(<ClubCalendar clubId={CLUB} />);

    const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
    expect(panel.parentElement).toHaveClass("order-first", "lg:sticky", "lg:order-none");
  });

  it("closes the details with Escape and gives focus back to the booking that was open (UX-004)", async () => {
    serveCalendar({ bookings: [booking()] });
    server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
    renderWithProviders(<ClubCalendar clubId={CLUB} />);
    await userEvent.click(await screen.findByRole("button", { name: /Rita Jogadora/ }));
    const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
    await within(panel).findByText("Rita Jogadora");

    await userEvent.keyboard("{Escape}");

    await waitFor(() => expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument());
    expect(screen.getByRole("button", { name: /Rita Jogadora/ })).toHaveFocus();
  });

  it("gives focus back to the booking when «Fechar» is pressed too", async () => {
    serveCalendar({ bookings: [booking()] });
    server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
    renderWithProviders(<ClubCalendar clubId={CLUB} />);
    await userEvent.click(await screen.findByRole("button", { name: /Rita Jogadora/ }));
    const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });

    await userEvent.click(within(panel).getByRole("button", { name: "Fechar" }));

    await waitFor(() => expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument());
    expect(screen.getByRole("button", { name: /Rita Jogadora/ })).toHaveFocus();
  });
});
