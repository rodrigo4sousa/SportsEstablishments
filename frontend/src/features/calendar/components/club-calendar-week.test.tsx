import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { addDaysToDateKey } from "@/lib/datetime";
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
const BASE = `/manage/clubs/${CLUB}/calendar`;

type PitchSpec = { id: string; name: string; sport: string; status?: string; hours: [string, string] | null; closedOn?: number[] };

const padel: PitchSpec = { id: PADEL, name: "Padel 1", sport: "padel", hours: ["09:00", "13:00"] };
const tennis: PitchSpec = { id: TENNIS, name: "Ténis 1", sport: "tennis", hours: ["10:00", "12:00"] };

/** 10:00-11:00 in Lisbon (WEST) on Tuesday 5 May 2026. */
const booking = (patch: Partial<CalendarBooking> = {}): CalendarBooking => ({
  id: FIRST,
  pitchId: PADEL,
  startsAt: "2026-05-05T09:00:00Z",
  endsAt: "2026-05-05T10:00:00Z",
  status: "confirmed",
  price: 18.5,
  playerName: "Rita Jogadora",
  createdByClub: false,
  ...patch,
});

const block = (patch: Partial<CalendarBlock> = {}): CalendarBlock => ({
  id: BLOCK,
  pitchId: PADEL,
  startsAt: "2026-05-07T10:00:00Z",
  endsAt: "2026-05-07T11:00:00Z",
  reason: "Manutenção da rede",
  ...patch,
});

const details = (patch: Partial<ClubBooking> = {}): ClubBooking => ({
  id: FIRST,
  pitchId: PADEL,
  pitchName: "Padel 1",
  sport: "padel",
  startsAt: "2026-05-05T09:00:00Z",
  endsAt: "2026-05-05T10:00:00Z",
  price: 18.5,
  status: "confirmed",
  playerName: "Rita Jogadora",
  createdByClub: false,
  customerPhone: null,
  createdAt: "2026-05-01T09:30:00Z",
  cancelledAt: null,
  cancellationReason: null,
  canCancel: true,
  ...patch,
});

type World = { pitches?: PitchSpec[]; bookings?: CalendarBooking[]; blocks?: CalendarBlock[] };

/** Serves the calendar like the API does: the days from `from` to `to`, each with the pitch's hours; cancelled bookings only when asked for. */
function serveCalendar(world: World = {}) {
  const asked: URLSearchParams[] = [];
  server.use(
    http.get(`/api/manage/clubs/${CLUB}/calendar`, ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.push(query);
      const from = query.get("from") ?? "";
      const to = query.get("to") ?? from;
      const dates: string[] = [];
      for (let date = from; date <= to; date = addDaysToDateKey(date, 1)) dates.push(date);
      const includeCancelled = query.get("includeCancelled") === "true";
      return HttpResponse.json({
        from,
        to,
        pitches: (world.pitches ?? [padel, tennis]).map((pitch) => ({
          id: pitch.id,
          name: pitch.name,
          sport: pitch.sport,
          status: pitch.status ?? "active",
          slotDurationMinutes: 60,
          days: dates.map((date, index) => ({
            date,
            opens: pitch.hours && !pitch.closedOn?.includes(index) ? pitch.hours[0] : null,
            closes: pitch.hours && !pitch.closedOn?.includes(index) ? pitch.hours[1] : null,
          })),
        })),
        bookings: (world.bookings ?? []).filter((candidate) => includeCancelled || !candidate.status.startsWith("cancelled")),
        blocks: world.blocks ?? [],
      });
    }),
  );
  return asked;
}

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

const dayColumn = (label: string) => screen.getByRole("group", { name: label });
const topOf = (element: HTMLElement) => Number.parseFloat(element.style.top);
const hourLabels = () => within(screen.getByRole("list", { name: "Horas" })).getAllByRole("listitem").map((item) => item.textContent);
const pitchSelect = () => screen.getByLabelText("Campo") as HTMLSelectElement;
const WEEK_HEADING = (level = 2) => screen.getByRole("heading", { level });

describe("ClubCalendar, weekly view", () => {
  beforeEach(() => {
    // 10:00 UTC on Monday 4 May 2026 is 11:00 in Lisbon.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-04T10:00:00Z"));
    resetNavigation(BASE);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("switching between the daily and the weekly view", () => {
    it("opens on the daily view, and «Vista semanal» shows the week of the day, Monday to Sunday, with the first pitch", async () => {
      resetNavigation(`${BASE}?date=2026-05-07`);
      const asked = serveCalendar({ bookings: [booking()] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });
      expect(screen.getByRole("button", { name: "Vista diária" })).toHaveAttribute("aria-pressed", "true");
      expect(screen.getByRole("button", { name: "Vista semanal" })).toHaveAttribute("aria-pressed", "false");
      expect(screen.queryByLabelText("Campo")).not.toBeInTheDocument();
      expect(asked[0].get("from")).toBe("2026-05-07");
      expect(asked[0].get("to")).toBe("2026-05-07");

      await userEvent.click(screen.getByRole("button", { name: "Vista semanal" }));

      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });
      expect(asked.at(-1)?.get("from")).toBe("2026-05-04");
      expect(asked.at(-1)?.get("to")).toBe("2026-05-10");
      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-05-07`);
      expect(screen.getByRole("button", { name: "Vista semanal" })).toHaveAttribute("aria-pressed", "true");
      expect(pitchSelect().value).toBe(PADEL);
      expect(WEEK_HEADING()).toHaveTextContent("segunda-feira, 4 de maio – domingo, 10 de maio (04/05/2026 – 10/05/2026)");
    });

    it("goes back to the daily view with «Vista diária», on the same date and with no pitch in the address", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-05-07&pitch=${TENNIS}`);
      serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });

      await userEvent.click(screen.getByRole("button", { name: "Vista diária" }));

      await screen.findByRole("group", { name: "Padel 1" });
      expect(currentPath()).toBe(`${BASE}?date=2026-05-07`);
    });

    it("keeps the cancelled toggle when the view changes", async () => {
      resetNavigation(`${BASE}?cancelled=1`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      await userEvent.click(screen.getByRole("button", { name: "Vista semanal" }));

      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });
      expect(currentPath()).toBe(`${BASE}?view=week&cancelled=1`);
      expect(asked.at(-1)?.get("includeCancelled")).toBe("true");
    });
  });

  describe("the week", () => {
    it("has seven columns, Monday first, named in Portuguese, with the pitch's bookings and blocks in the right days", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({
        bookings: [booking(), booking({ id: SECOND, pitchId: TENNIS, playerName: "Bruno Jogador" })],
        blocks: [block()],
      });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("button", { name: /Rita Jogadora/ });

      const names = ["Segunda-feira, 04/05/2026", "Terça-feira, 05/05/2026", "Quarta-feira, 06/05/2026", "Quinta-feira, 07/05/2026", "Sexta-feira, 08/05/2026", "Sábado, 09/05/2026", "Domingo, 10/05/2026"];
      for (const name of names) {
        expect(dayColumn(name)).toBeInTheDocument();
      }
      expect(screen.getAllByRole("group").filter((group) => group.hasAttribute("data-date"))).toHaveLength(7);
      // Tuesday has the booking, Thursday the block, and the other pitch's booking is not here.
      expect(within(dayColumn(names[1])).getByRole("button", { name: "Rita Jogadora, 10:00 – 11:00, Confirmada" })).toBeInTheDocument();
      expect(within(dayColumn(names[3])).getByRole("group", { name: "Bloqueio, 11:00 – 12:00, Manutenção da rede" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /Bruno Jogador/ })).not.toBeInTheDocument();
      expect(within(dayColumn(names[0])).queryByRole("button")).not.toBeInTheDocument();
      expect(hourLabels()).toEqual(["09:00", "10:00", "11:00", "12:00"]);
    });

    it("positions the boxes like the daily view does: by elapsed time from the top of the shared grid", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ bookings: [booking()], blocks: [block()] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const box = await screen.findByRole("button", { name: /Rita Jogadora/ });
      const held = screen.getByRole("group", { name: /Bloqueio/ });

      // The grid starts at 09:00: a booking at 10:00 is 64 px down, one hour tall; a block at 11:00 is 128 px down.
      expect(topOf(box)).toBe(64);
      expect(Number.parseFloat(box.style.height)).toBe(64);
      expect(topOf(held)).toBe(128);
      expect(box).toHaveAttribute("data-minute-start", "600");
    });

    it("styles a block differently, with no button to open it", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ blocks: [block()] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const held = await screen.findByRole("group", { name: /Bloqueio/ });

      expect(held.tagName).not.toBe("BUTTON");
      expect(held.className).toContain("border-dashed");
      expect(held).toHaveTextContent("Manutenção da rede");
    });

    it("marks a day the pitch is closed, and shades what is outside its hours", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ pitches: [{ ...padel, closedOn: [6] }, tennis] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Domingo, 10/05/2026" });

      expect(within(dayColumn("Domingo, 10/05/2026")).getByText("Fechado")).toBeInTheDocument();
      expect(within(dayColumn("Segunda-feira, 04/05/2026")).queryByText("Fechado")).not.toBeInTheDocument();
      expect(dayColumn("Segunda-feira, 04/05/2026").querySelector("[data-open-range]")).not.toBeNull();
    });

    it("says when the pitch is closed all week and has nothing", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ pitches: [{ ...padel, hours: null }] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByText("Este campo não abre nesta semana e não tem reservas nem bloqueios.")).toBeInTheDocument();
    });

    it("says when the club has no pitches, and offers no list of pitches", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ pitches: [] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByText("Este clube ainda não tem campos ativos.")).toBeInTheDocument();
      expect(screen.queryByLabelText("Campo")).not.toBeInTheDocument();
    });

    it("shows the not-found page for a club the API answers 404 for", async () => {
      resetNavigation(`${BASE}?view=week`);
      server.use(http.get(`/api/manage/clubs/${CLUB}/calendar`, () => problem(404, "club_not_found")));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      expect(await screen.findByRole("heading", { name: "Clube não encontrado" })).toBeInTheDocument();
    });
  });

  describe("choosing the pitch", () => {
    it("lists the pitches, and switching shows the other pitch's week and puts it in the address", async () => {
      resetNavigation(`${BASE}?view=week`);
      const asked = serveCalendar({
        bookings: [booking(), booking({ id: SECOND, pitchId: TENNIS, playerName: "Bruno Jogador", startsAt: "2026-05-06T09:00:00Z", endsAt: "2026-05-06T10:00:00Z" })],
      });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("button", { name: /Rita Jogadora/ });
      expect([...pitchSelect().options].map((option) => option.textContent)).toEqual(["Padel 1 · Padel", "Ténis 1 · Ténis"]);

      await userEvent.selectOptions(pitchSelect(), TENNIS);

      expect(await screen.findByRole("button", { name: /Bruno Jogador/ })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /Rita Jogadora/ })).not.toBeInTheDocument();
      expect(currentPath()).toBe(`${BASE}?view=week&pitch=${TENNIS}`);
      expect(pitchSelect().value).toBe(TENNIS);
      // The grid follows the chosen pitch's hours (10:00-12:00), and no new request is needed: the week has every pitch.
      expect(hourLabels()).toEqual(["10:00", "11:00"]);
      expect(asked).toHaveLength(1);
    });

    it("opens on the pitch the address names, and on the first one for a pitch that is not in the club", async () => {
      resetNavigation(`${BASE}?view=week&pitch=${TENNIS}`);
      serveCalendar();
      const first = renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByLabelText("Campo");
      expect(pitchSelect().value).toBe(TENNIS);
      first.unmount();

      resetNavigation(`${BASE}?view=week&pitch=0199f7a0-0000-7000-8000-00000000ffff`);
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByLabelText("Campo");
      expect(pitchSelect().value).toBe(PADEL);
    });

    it("marks an inactive pitch that is still listed", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ pitches: [padel, { ...tennis, status: "inactive" }] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByLabelText("Campo");

      expect([...pitchSelect().options].map((option) => option.textContent)).toEqual(["Padel 1 · Padel", "Ténis 1 · Ténis · Inativo"]);
    });

    it("closes the open booking when another pitch is chosen", async () => {
      resetNavigation(`${BASE}?view=week&booking=${FIRST}`);
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("region", { name: "Detalhes da reserva" });
      await screen.findByLabelText("Campo");

      await userEvent.selectOptions(pitchSelect(), TENNIS);

      await waitFor(() => expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument());
      expect(currentPath()).toBe(`${BASE}?view=week&pitch=${TENNIS}`);
    });
  });

  describe("navigating by week", () => {
    it("goes to the week after and the week before, in the address and in the request", async () => {
      resetNavigation(`${BASE}?view=week`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });
      expect(screen.getByRole("button", { name: "Semana seguinte" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Dia seguinte" })).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole("button", { name: "Semana seguinte" }));

      await screen.findByRole("group", { name: "Segunda-feira, 11/05/2026" });
      expect(asked.at(-1)?.get("from")).toBe("2026-05-11");
      expect(asked.at(-1)?.get("to")).toBe("2026-05-17");
      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-05-11`);
      expect(WEEK_HEADING()).toHaveTextContent("segunda-feira, 11 de maio – domingo, 17 de maio");

      await userEvent.click(screen.getByRole("button", { name: "Semana anterior" }));
      await userEvent.click(screen.getByRole("button", { name: "Semana anterior" }));

      await screen.findByRole("group", { name: "Segunda-feira, 27/04/2026" });
      expect(asked.at(-1)?.get("from")).toBe("2026-04-27");
      expect(asked.at(-1)?.get("to")).toBe("2026-05-03");
      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-04-27`);
    });

    it("keeps the chosen pitch while moving between weeks", async () => {
      resetNavigation(`${BASE}?view=week&pitch=${TENNIS}`);
      serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByLabelText("Campo");

      await userEvent.click(screen.getByRole("button", { name: "Semana seguinte" }));

      await screen.findByRole("group", { name: "Segunda-feira, 11/05/2026" });
      expect(pitchSelect().value).toBe(TENNIS);
      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-05-11&pitch=${TENNIS}`);
    });

    it("goes back to the week of today with «Hoje», and today is not written in the address", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-25`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Segunda-feira, 19/10/2026" });

      await userEvent.click(screen.getByRole("button", { name: "Hoje" }));

      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });
      expect(asked.at(-1)?.get("from")).toBe("2026-05-04");
      expect(currentPath()).toBe(`${BASE}?view=week`);
    });

    it("uses the Lisbon date for «Hoje»: on Sunday night in Lisbon it is already the next week", async () => {
      // 23:30 UTC on Sunday 10 May is 00:30 on Monday 11 May in Lisbon.
      vi.setSystemTime(new Date("2026-05-10T23:30:00Z"));
      resetNavigation(`${BASE}?view=week&date=2026-05-04`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });

      await userEvent.click(screen.getByRole("button", { name: "Hoje" }));

      await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-05-11"));
    });

    it("goes to the week of a date typed in the date field, and shows the Monday of that week", async () => {
      resetNavigation(`${BASE}?view=week`);
      const asked = serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Segunda-feira, 04/05/2026" });

      fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-05-20" } });

      await screen.findByRole("group", { name: "Segunda-feira, 18/05/2026" });
      expect(asked.at(-1)?.get("from")).toBe("2026-05-18");
      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-05-20`);
    });

    it("closes the open booking when the week changes", async () => {
      resetNavigation(`${BASE}?view=week&booking=${FIRST}`);
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("region", { name: "Detalhes da reserva" });

      await userEvent.click(screen.getByRole("button", { name: "Semana seguinte" }));

      await waitFor(() => expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument());
      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-05-11`);
    });
  });

  describe("cancelled bookings", () => {
    it("are hidden by default and shown, marked, with the toggle", async () => {
      resetNavigation(`${BASE}?view=week`);
      const asked = serveCalendar({
        bookings: [
          booking(),
          booking({ id: SECOND, status: "cancelled_by_club", playerName: "Ana Cancelada", startsAt: "2026-05-06T09:00:00Z", endsAt: "2026-05-06T10:00:00Z" }),
        ],
      });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("button", { name: /Rita Jogadora/ });
      expect(screen.queryByRole("button", { name: /Ana Cancelada/ })).not.toBeInTheDocument();

      await userEvent.click(screen.getByLabelText("Mostrar reservas canceladas"));

      expect(await screen.findByRole("button", { name: "Ana Cancelada, 10:00 – 11:00, Cancelada pelo clube" })).toBeInTheDocument();
      expect(asked.at(-1)?.get("includeCancelled")).toBe("true");
      expect(currentPath()).toBe(`${BASE}?view=week&cancelled=1`);
    });
  });

  describe("a week with the day the clocks go back (Monday 2026-10-19 to Sunday 2026-10-25)", () => {
    const sundayBookings = [
      booking({ id: FIRST, startsAt: "2026-10-25T00:30:00Z", endsAt: "2026-10-25T01:00:00Z", playerName: "Primeira" }), // 01:30 WEST
      booking({ id: SECOND, startsAt: "2026-10-25T01:30:00Z", endsAt: "2026-10-25T02:00:00Z", playerName: "Segunda" }), // 01:30 WET
      booking({ id: THIRD, startsAt: "2026-10-25T09:00:00Z", endsAt: "2026-10-25T10:00:00Z", playerName: "Manhã" }), // 09:00 WET
    ];

    it("asks for Monday to Sunday and shows seven columns, the Sunday marked as 25 hours long", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-22`);
      const asked = serveCalendar({ bookings: sundayBookings });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Domingo, 25/10/2026" });

      expect(asked[0].get("from")).toBe("2026-10-19");
      expect(asked[0].get("to")).toBe("2026-10-25");
      expect(screen.getAllByRole("group").filter((group) => group.hasAttribute("data-date")).map((group) => group.getAttribute("data-date"))).toEqual([
        "2026-10-19",
        "2026-10-20",
        "2026-10-21",
        "2026-10-22",
        "2026-10-23",
        "2026-10-24",
        "2026-10-25",
      ]);
      expect(within(dayColumn("Domingo, 25/10/2026")).getByText("25/10/2026 · 25 h")).toBeInTheDocument();
      expect(within(dayColumn("Sábado, 24/10/2026")).getByText("24/10/2026")).toBeInTheDocument();
      expect(WEEK_HEADING()).toHaveTextContent("segunda-feira, 19 de outubro – domingo, 25 de outubro (19/10/2026 – 25/10/2026)");
    });

    it("tells the owner the clocks change, so the shifted scale is not a surprise", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-22`);
      serveCalendar({ bookings: sundayBookings });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const note = await screen.findByRole("note");

      expect(note).toHaveTextContent("Nesta semana o relógio muda (25/10/2026, com 25 horas)");
      expect(note).toHaveTextContent("a hora escrita em cada reserva é a certa");
    });

    it("has no such note in an ordinary week", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-27`);
      serveCalendar();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Segunda-feira, 26/10/2026" });

      expect(screen.queryByRole("note")).not.toBeInTheDocument();
    });

    it("lays out the Sunday by elapsed time: the two 01:30 an hour apart, each with its own clock time", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-22`);
      serveCalendar({ pitches: [{ ...padel, hours: ["00:00", "00:00"] }], bookings: sundayBookings });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const first = await screen.findByRole("button", { name: /Primeira/ });
      const second = screen.getByRole("button", { name: /Segunda/ });
      const morning = screen.getByRole("button", { name: /Manhã/ });

      expect(within(dayColumn("Domingo, 25/10/2026")).getAllByRole("button")).toHaveLength(3);
      expect(topOf(first)).toBeCloseTo(96); // 90 minutes into the day
      expect(topOf(second)).toBeCloseTo(160); // 150 minutes: an hour later, not half an hour
      expect(topOf(morning)).toBeCloseTo(640); // 09:00 WET is 600 minutes into this 25-hour day
      expect(first).toHaveAccessibleName("Primeira, 01:30 (verão) – 01:00 (inverno), Confirmada");
      expect(second).toHaveAccessibleName("Segunda, 01:30 (inverno) – 02:00, Confirmada");
      expect(morning).toHaveAccessibleName("Manhã, 09:00 – 10:00, Confirmada");
    });

    it("draws the same clock hour an hour lower on the Sunday than on the Saturday, because the day is longer", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-22`);
      serveCalendar({
        bookings: [
          booking({ id: FIRST, startsAt: "2026-10-24T08:00:00Z", endsAt: "2026-10-24T09:00:00Z", playerName: "Sábado" }), // 09:00 WEST
          booking({ id: SECOND, startsAt: "2026-10-25T09:00:00Z", endsAt: "2026-10-25T10:00:00Z", playerName: "Domingo" }), // 09:00 WET
        ],
      });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const saturday = await screen.findByRole("button", { name: /Sábado/ });
      const sunday = screen.getByRole("button", { name: /Domingo/ });

      expect(saturday).toHaveAccessibleName("Sábado, 09:00 – 10:00, Confirmada");
      expect(sunday).toHaveAccessibleName("Domingo, 09:00 – 10:00, Confirmada");
      expect(topOf(sunday) - topOf(saturday)).toBeCloseTo(64);
    });

    it("labels the hour scale with the clock of an ordinary day", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-10-22`);
      serveCalendar({ pitches: [{ ...padel, hours: ["00:00", "00:00"] }] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await screen.findByRole("group", { name: "Domingo, 25/10/2026" });

      const labels = hourLabels();
      expect(labels).toHaveLength(25);
      expect(labels.slice(0, 3)).toEqual(["00:00", "01:00", "02:00"]);
    });
  });

  describe("a week with the day the clocks go forward (Monday 2026-03-23 to Sunday 2026-03-29)", () => {
    it("shows the Sunday as 23 hours long, with the booking at 02:00 one hour into its day", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-03-25`);
      const asked = serveCalendar({
        pitches: [{ ...padel, hours: ["00:00", "00:00"] }],
        bookings: [booking({ id: FIRST, startsAt: "2026-03-29T01:00:00Z", endsAt: "2026-03-29T02:00:00Z", playerName: "Cedo" })], // 02:00 WEST
      });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const early = await screen.findByRole("button", { name: /Cedo/ });

      expect(asked[0].get("from")).toBe("2026-03-23");
      expect(asked[0].get("to")).toBe("2026-03-29");
      expect(within(dayColumn("Domingo, 29/03/2026")).getByText("29/03/2026 · 23 h")).toBeInTheDocument();
      expect(screen.getByRole("note")).toHaveTextContent("29/03/2026, com 23 horas");
      expect(topOf(early)).toBeCloseTo(64);
      expect(early).toHaveAccessibleName("Cedo, 02:00 – 03:00, Confirmada");
    });
  });

  describe("opening a booking from the weekly view", () => {
    it("shows its details, with the weekly view and the booking in the address", async () => {
      resetNavigation(`${BASE}?view=week`);
      serveCalendar({ bookings: [booking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      await userEvent.click(await screen.findByRole("button", { name: /Rita Jogadora/ }));

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await within(panel).findByText("Rita Jogadora");
      expect(currentPath()).toBe(`${BASE}?view=week&booking=${FIRST}`);
      expect(screen.getByRole("button", { name: /Rita Jogadora/ })).toHaveAttribute("aria-pressed", "true");
      const lines = Object.fromEntries([...panel.querySelectorAll("dt")].map((term) => [term.textContent, (term.nextElementSibling?.textContent ?? "").replace(/\s/g, " ")]));
      expect(lines).toMatchObject({
        Jogador: "Rita Jogadora",
        Campo: "Padel 1 · Padel",
        Data: "terça-feira, 5 de maio (05/05/2026)",
        Hora: "10:00 – 11:00",
        Estado: "Confirmada",
      });
      await userEvent.click(within(panel).getByRole("button", { name: "Fechar" }));
      expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument();
      expect(currentPath()).toBe(`${BASE}?view=week`);
    });

    it("cancels it with a reason, and the calendar is loaded again without it", async () => {
      resetNavigation(`${BASE}?view=week&booking=${FIRST}`);
      let isCancelled = false;
      serveCalendar({ bookings: [booking()] });
      let calendarRequests = 0;
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/calendar`, ({ request }) => {
          calendarRequests += 1;
          const query = new URL(request.url).searchParams;
          return HttpResponse.json({
            from: query.get("from"),
            to: query.get("to"),
            pitches: [
              {
                id: PADEL,
                name: "Padel 1",
                sport: "padel",
                status: "active",
                slotDurationMinutes: 60,
                days: ["2026-05-04", "2026-05-05", "2026-05-06", "2026-05-07", "2026-05-08", "2026-05-09", "2026-05-10"].map((date) => ({ date, opens: "09:00", closes: "13:00" })),
              },
            ],
            bookings: isCancelled ? [] : [booking()],
            blocks: [],
          });
        }),
        http.get(`/api/manage/clubs/${CLUB}/bookings/${FIRST}`, () =>
          HttpResponse.json(isCancelled ? details({ status: "cancelled_by_club", canCancel: false, cancellationReason: "Campo em obras", cancelledAt: "2026-05-04T10:00:00Z" }) : details()),
        ),
        http.post(`/api/manage/clubs/${CLUB}/bookings/${FIRST}/cancel`, async ({ request }) => {
          expect(await request.json()).toEqual({ reason: "Campo em obras" });
          isCancelled = true;
          return HttpResponse.json(details({ status: "cancelled_by_club", canCancel: false, cancellationReason: "Campo em obras", cancelledAt: "2026-05-04T10:00:00Z" }));
        }),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await screen.findByRole("button", { name: /Rita Jogadora/ });
      await userEvent.click(await within(panel).findByRole("button", { name: "Cancelar reserva" }));
      await userEvent.type(screen.getByLabelText("Motivo do cancelamento"), "Campo em obras");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await within(panel).findByText("Reserva cancelada. O horário voltou a ficar livre.")).toBeInTheDocument();
      await waitFor(() => expect(screen.queryByRole("button", { name: /Rita Jogadora/ })).not.toBeInTheDocument());
      expect(within(panel).getByText("Cancelada pelo clube")).toBeInTheDocument();
      expect(calendarRequests).toBeGreaterThan(1);
    });
  });
});
