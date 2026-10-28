import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { addDaysToDateKey } from "@/lib/datetime";
import { currentPath, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { CalendarBooking, ClubBooking } from "../types";
import { ClubCalendar } from "./club-calendar";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const CLUB = "0199f7a0-0000-7000-8000-0000000000c1";
const PADEL = "0199f7a0-0000-7000-8000-0000000000d1";
const TENNIS = "0199f7a0-0000-7000-8000-0000000000d2";
const OLD = "0199f7a0-0000-7000-8000-0000000000d3";
const PLAYER_BOOKING = "0199f7a0-0000-7000-8000-0000000000b1";
const CUSTOMER_BOOKING = "0199f7a0-0000-7000-8000-0000000000b2";
const NEW_ID = "0199f7a0-0000-7000-8000-0000000000b9";
const BASE = `/manage/clubs/${CLUB}/calendar`;

type PitchSpec = { id: string; name: string; sport: string; status?: string; hours: [string, string] };
const padel: PitchSpec = { id: PADEL, name: "Padel 1", sport: "padel", hours: ["09:00", "13:00"] };
const tennis: PitchSpec = { id: TENNIS, name: "Ténis 1", sport: "tennis", hours: ["10:00", "12:00"] };

/** 10:00-11:00 in Lisbon (WEST) on Tuesday 5 May 2026. */
const booking = (patch: Partial<CalendarBooking> = {}): CalendarBooking => ({
  id: PLAYER_BOOKING,
  pitchId: PADEL,
  startsAt: "2026-05-05T09:00:00Z",
  endsAt: "2026-05-05T10:00:00Z",
  status: "confirmed",
  price: 18.5,
  bookerName: "Rita Jogadora",
  createdByClub: false,
  ...patch,
});

const customerBooking = (patch: Partial<CalendarBooking> = {}): CalendarBooking =>
  booking({ id: CUSTOMER_BOOKING, startsAt: "2026-05-05T10:00:00Z", endsAt: "2026-05-05T11:00:00Z", bookerName: "Maria Cliente", createdByClub: true, ...patch });

const details = (patch: Partial<ClubBooking> = {}): ClubBooking => ({
  id: CUSTOMER_BOOKING,
  pitchId: PADEL,
  pitchName: "Padel 1",
  sport: "padel",
  startsAt: "2026-05-05T10:00:00Z",
  endsAt: "2026-05-05T11:00:00Z",
  price: 18.5,
  status: "confirmed",
  bookerName: "Maria Cliente",
  createdByClub: true,
  customerPhone: "912345678",
  createdAt: "2026-05-04T09:30:00Z",
  cancelledAt: null,
  cancellationReason: null,
  canCancel: true,
  canMarkNoShow: false,
  canUndoNoShow: false,
  playerNoShows: null,
  ...patch,
});

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

type World = { pitches?: PitchSpec[]; bookings?: CalendarBooking[] };

/** Serves the calendar, the owner's slot preview and the details; the bookings can change while a test runs. */
function serve(world: World = {}) {
  const state = { bookings: world.bookings ?? [] };
  const asked: URLSearchParams[] = [];
  const pitches = world.pitches ?? [padel, tennis];
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
        pitches: pitches.map((pitch) => ({
          id: pitch.id,
          name: pitch.name,
          sport: pitch.sport,
          status: pitch.status ?? "active",
          slotDurationMinutes: 60,
          days: dates.map((date) => ({ date, opens: pitch.hours[0], closes: pitch.hours[1] })),
        })),
        bookings: state.bookings.filter((candidate) => includeCancelled || !candidate.status.startsWith("cancelled")),
        blocks: [],
      });
    }),
    http.get(`/api/manage/clubs/${CLUB}/pitches/:pitchId/slots`, ({ params, request }) => {
      const date = new URL(request.url).searchParams.get("date") ?? "";
      const pitch = pitches.find((candidate) => candidate.id === params.pitchId);
      const first = Number(pitch?.hours[0].slice(0, 2));
      const last = Number(pitch?.hours[1].slice(0, 2));
      const slots = Array.from({ length: Math.max(0, last - first) }, (_, index) => {
        const startsAt = `${date}T${String(first + index - 1).padStart(2, "0")}:00:00Z`; // Lisbon is UTC+1 in May
        const endsAt = `${date}T${String(first + index).padStart(2, "0")}:00:00Z`;
        const booked = state.bookings.some((candidate) => candidate.pitchId === params.pitchId && candidate.status === "confirmed" && candidate.startsAt < endsAt && startsAt < candidate.endsAt);
        return { startsAt, endsAt, blocked: false, booked, price: 20 };
      });
      return HttpResponse.json({ date, slotDurationMinutes: 60, slots });
    }),
  );
  return { state, asked };
}

const toolbarButton = () => screen.getByRole("button", { name: "Nova reserva" });
const form = () => screen.getByRole("form", { name: "Nova reserva" });
const emptyCell = (name: string) => screen.getByRole("button", { name });
const cells = () => screen.queryAllByRole("button", { name: /^Nova reserva: / });
const detailsPanel = () => screen.getByRole("region", { name: "Detalhes da reserva" });

describe("ClubCalendar, bookings for customers (US13)", () => {
  beforeEach(() => {
    // 10:00 UTC on Monday 4 May 2026 is 11:00 in Lisbon.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-04T10:00:00Z"));
    resetNavigation(BASE);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the «Nova reserva» action", () => {
    it("opens the form on the first pitch and the day shown, in the daily view", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      serve();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      await userEvent.click(toolbarButton());

      expect(form()).toBeInTheDocument();
      expect((within(form()).getByLabelText("Campo") as HTMLSelectElement).value).toBe(PADEL);
      expect(within(form()).getByLabelText("Data")).toHaveValue("2026-05-05");
      expect(await within(form()).findByRole("button", { name: "09:00 – 10:00 · 20,00 €" })).toBeEnabled();
    });

    it("opens the form on the pitch of the weekly view and the day shown", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-05-06&pitch=${TENNIS}`);
      serve();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Quarta-feira, 06/05/2026" });

      await userEvent.click(toolbarButton());

      expect((within(form()).getByLabelText("Campo") as HTMLSelectElement).value).toBe(TENNIS);
      expect(within(form()).getByLabelText("Data")).toHaveValue("2026-05-06");
    });

    it("uses today when the day shown is outside the days a booking can be made", async () => {
      resetNavigation(`${BASE}?date=2026-09-01`);
      serve();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      await userEvent.click(toolbarButton());

      expect(within(form()).getByLabelText("Data")).toHaveValue("2026-05-04");
    });

    it("lists only the active pitches in the form, and is disabled when there are none", async () => {
      serve({ pitches: [{ ...padel, status: "inactive" }, tennis, { id: OLD, name: "Campo velho", sport: "futsal", status: "inactive", hours: ["09:00", "13:00"] }] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Ténis 1" });

      await userEvent.click(toolbarButton());

      const select = within(form()).getByLabelText("Campo") as HTMLSelectElement;
      expect([...select.options].map((option) => option.textContent)).toEqual(["Ténis 1 · Ténis"]);
    });

    it("is disabled when no pitch of the club is active", async () => {
      serve({ pitches: [{ ...padel, status: "inactive" }] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      expect(toolbarButton()).toBeDisabled();
    });

    it("closes with «Fechar» and closes the open booking's details when it opens", async () => {
      resetNavigation(`${BASE}?date=2026-05-05&booking=${CUSTOMER_BOOKING}`);
      serve({ bookings: [customerBooking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${CUSTOMER_BOOKING}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("region", { name: "Detalhes da reserva" });

      await userEvent.click(toolbarButton());

      await waitFor(() => expect(screen.queryByRole("region", { name: "Detalhes da reserva" })).not.toBeInTheDocument());
      expect(currentPath()).toBe(`${BASE}?date=2026-05-05`);
      await userEvent.click(within(form()).getByRole("button", { name: "Fechar" }));
      expect(screen.queryByRole("form", { name: "Nova reserva" })).not.toBeInTheDocument();
    });
  });

  describe("clicking an empty time", () => {
    it("opens the form for that pitch, day and hour, with the slot chosen, in the daily view", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      serve({ bookings: [booking()] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      await userEvent.click(emptyCell("Nova reserva: Ténis 1, 11:00"));

      expect((within(form()).getByLabelText("Campo") as HTMLSelectElement).value).toBe(TENNIS);
      expect(within(form()).getByLabelText("Data")).toHaveValue("2026-05-05");
      await waitFor(() => expect(within(form()).getByRole("button", { name: "11:00 – 12:00 · 20,00 €" })).toHaveAttribute("aria-pressed", "true"));
    });

    it("does not choose the slot when that hour is already booked", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      serve({ bookings: [booking()] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("button", { name: /Rita Jogadora/ });

      // The cell lies under the booking, so a mouse click lands on the booking; the cell itself is still reachable for assistive technology.
      await userEvent.click(emptyCell("Nova reserva: Padel 1, 10:00"));

      expect(await within(form()).findByRole("button", { name: "10:00 – 11:00 · 20,00 € Reservado" })).toBeDisabled();
      expect(within(form()).queryByRole("button", { pressed: true })).not.toBeInTheDocument();
    });

    it("is offered only inside the pitch's opening hours", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      serve(); // Padel 09:00-13:00, Ténis 10:00-12:00, so the grid is 09:00-13:00
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      const names = cells().map((cell) => cell.getAttribute("aria-label"));

      expect(names.filter((name) => name?.includes("Padel 1"))).toEqual([
        "Nova reserva: Padel 1, 09:00",
        "Nova reserva: Padel 1, 10:00",
        "Nova reserva: Padel 1, 11:00",
        "Nova reserva: Padel 1, 12:00",
      ]);
      expect(names.filter((name) => name?.includes("Ténis 1"))).toEqual(["Nova reserva: Ténis 1, 10:00", "Nova reserva: Ténis 1, 11:00"]);
    });

    it("is not offered for hours that are over, today", async () => {
      serve(); // it is 11:00 in Lisbon on 4 May: the 09:00 and 10:00 hours are over
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });

      const names = cells().map((cell) => cell.getAttribute("aria-label"));

      expect(names).not.toContain("Nova reserva: Padel 1, 09:00");
      expect(names).not.toContain("Nova reserva: Padel 1, 10:00");
      expect(names).toContain("Nova reserva: Padel 1, 11:00"); // the current hour: part of it is still to come
      expect(names).toContain("Nova reserva: Padel 1, 12:00");
    });

    it("is not offered for days past 30 days ahead, nor for the past", async () => {
      resetNavigation(`${BASE}?date=2026-06-10`);
      serve();
      const first = renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });
      expect(cells()).toHaveLength(0);
      first.unmount();

      resetNavigation(`${BASE}?date=2026-05-01`);
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });
      expect(cells()).toHaveLength(0);
    });

    it("is not offered in the column of an inactive pitch", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      serve({ pitches: [padel, { ...tennis, status: "inactive" }] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Ténis 1" });

      const names = cells().map((cell) => cell.getAttribute("aria-label"));

      expect(names.some((name) => name?.includes("Ténis 1"))).toBe(false);
      expect(names.some((name) => name?.includes("Padel 1"))).toBe(true);
    });

    it("works in the weekly view too: the pitch is the chosen one and the day is the column's", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-05-05&pitch=${TENNIS}`);
      serve();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Terça-feira, 05/05/2026" });

      await userEvent.click(emptyCell("Nova reserva: Terça-feira, 05/05/2026, 11:00"));

      expect((within(form()).getByLabelText("Campo") as HTMLSelectElement).value).toBe(TENNIS);
      expect(within(form()).getByLabelText("Data")).toHaveValue("2026-05-05");
      await waitFor(() => expect(within(form()).getByRole("button", { name: "11:00 – 12:00 · 20,00 €" })).toHaveAttribute("aria-pressed", "true"));
    });

    it("is not offered in the weekly view for days outside the booking window or the pitch's closed days", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-06-10`);
      serve();
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Segunda-feira, 08/06/2026" });

      expect(cells()).toHaveLength(0);
    });
  });

  describe("after creating", () => {
    async function createFromToolbar(initialUrl: string) {
      resetNavigation(initialUrl);
      const { state, asked } = serve({ bookings: [booking()] });
      const bodies: unknown[] = [];
      server.use(
        http.post(`/api/manage/clubs/${CLUB}/bookings`, async ({ request }) => {
          bodies.push(await request.json());
          state.bookings.push(customerBooking({ id: NEW_ID, startsAt: "2026-05-06T10:00:00Z", endsAt: "2026-05-06T11:00:00Z" }));
          return HttpResponse.json(details({ id: NEW_ID, startsAt: "2026-05-06T10:00:00Z", endsAt: "2026-05-06T11:00:00Z" }), { status: 201 });
        }),
        http.get(`/api/manage/clubs/${CLUB}/bookings/${NEW_ID}`, () => HttpResponse.json(details({ id: NEW_ID, startsAt: "2026-05-06T10:00:00Z", endsAt: "2026-05-06T11:00:00Z" }))),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await waitFor(() => expect(toolbarButton()).toBeEnabled());
      await userEvent.click(toolbarButton());
      fireEvent.change(within(form()).getByLabelText("Data"), { target: { value: "2026-05-06" } });
      await userEvent.click(await within(form()).findByRole("button", { name: "11:00 – 12:00 · 20,00 €" }));
      await userEvent.type(within(form()).getByLabelText("Nome do cliente"), "Maria Cliente");
      await userEvent.type(within(form()).getByLabelText("Telemóvel do cliente"), "912 345 678");
      await userEvent.click(within(form()).getByRole("button", { name: "Criar reserva" }));
      return { bodies, asked };
    }

    it("shows the booking in the daily view on its day, marked «Criada pelo clube», with its details open", async () => {
      const { bodies } = await createFromToolbar(`${BASE}?date=2026-05-05`);

      expect(await screen.findByText("Reserva criada para Maria Cliente.")).toHaveAttribute("role", "status");

      expect(bodies).toEqual([{ pitchId: PADEL, startsAt: "2026-05-06T10:00:00Z", customerName: "Maria Cliente", customerPhone: "912345678" }]);
      expect(screen.queryByRole("form", { name: "Nova reserva" })).not.toBeInTheDocument();
      // The calendar went to the day of the booking, with the details open.
      expect(currentPath()).toBe(`${BASE}?date=2026-05-06&booking=${NEW_ID}`);
      expect(await screen.findByRole("heading", { level: 2, name: /quarta-feira, 6 de maio/ })).toBeInTheDocument();
      const box = await screen.findByRole("button", { name: "Maria Cliente, 11:00 – 12:00, Confirmada, criada pelo clube" });
      expect(within(column("Padel 1")).getByRole("button", { name: /Maria Cliente/ })).toBe(box);
      expect(box).toHaveTextContent("Criada pelo clube");
      const panel = detailsPanel();
      await within(panel).findByText("912345678");
      expect(within(panel).getByText("Cliente")).toBeInTheDocument();
      expect(within(panel).getByText("Criada pelo clube")).toBeInTheDocument();
    });

    it("keeps the weekly view, goes to the week and pitch of the new booking and shows it there", async () => {
      await createFromToolbar(`${BASE}?view=week&date=2026-05-05&pitch=${PADEL}`);

      expect(await screen.findByText("Reserva criada para Maria Cliente.")).toBeInTheDocument();

      expect(currentPath()).toBe(`${BASE}?view=week&date=2026-05-06&pitch=${PADEL}&booking=${NEW_ID}`);
      const box = await screen.findByRole("button", { name: /Maria Cliente, 11:00 – 12:00, Confirmada, criada pelo clube/ });
      expect(within(dayColumn("Quarta-feira, 06/05/2026")).getByRole("button", { name: /Maria Cliente/ })).toBe(box);
    });

    it("loads the calendar again so the booking is in it", async () => {
      const { asked } = await createFromToolbar(`${BASE}?date=2026-05-05`);

      await screen.findByText("Reserva criada para Maria Cliente.");

      expect(asked.some((query) => query.get("from") === "2026-05-06")).toBe(true);
    });

    it("on a conflict shows the message in the form, loads the slots again and the calendar, and keeps the form open", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      const { state, asked } = serve();
      server.use(
        http.post(`/api/manage/clubs/${CLUB}/bookings`, () => {
          // Somebody else took the slot first.
          state.bookings.push(booking({ id: PLAYER_BOOKING, startsAt: "2026-05-05T08:00:00Z", endsAt: "2026-05-05T09:00:00Z" }));
          return problem(409, "slot_taken");
        }),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("group", { name: "Padel 1" });
      await userEvent.click(toolbarButton());
      await userEvent.click(await within(form()).findByRole("button", { name: "09:00 – 10:00 · 20,00 €" }));
      await userEvent.type(within(form()).getByLabelText("Nome do cliente"), "Maria Cliente");
      await userEvent.type(within(form()).getByLabelText("Telemóvel do cliente"), "912345678");
      const askedBefore = asked.length;

      await userEvent.click(within(form()).getByRole("button", { name: "Criar reserva" }));

      expect(await within(form()).findByRole("alert")).toHaveTextContent("Este horário acabou de ser reservado. Escolhe outro.");
      expect(await within(form()).findByRole("button", { name: "09:00 – 10:00 · 20,00 € Reservado" })).toBeDisabled();
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
      expect(screen.getByRole("button", { name: /Rita Jogadora/ })).toBeInTheDocument();
      expect(screen.queryByText(/Reserva criada/)).not.toBeInTheDocument();
    });
  });

  describe("the marker «Criada pelo clube»", () => {
    it("is in the daily view on a booking the club made, and not on a player's", async () => {
      resetNavigation(`${BASE}?date=2026-05-05`);
      serve({ bookings: [booking(), customerBooking({ startsAt: "2026-05-05T10:00:00Z", endsAt: "2026-05-05T11:00:00Z", pitchId: TENNIS })] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const customer = await screen.findByRole("button", { name: "Maria Cliente, 11:00 – 12:00, Confirmada, criada pelo clube" });
      const player = screen.getByRole("button", { name: "Rita Jogadora, 10:00 – 11:00, Confirmada" });

      expect(customer).toHaveTextContent("Maria Cliente");
      expect(customer).toHaveTextContent("Criada pelo clube");
      expect(customer).toHaveAttribute("data-created-by-club", "true");
      expect(customer.className).toContain("border-l-4");
      expect(player).not.toHaveTextContent("Criada pelo clube");
      expect(player).not.toHaveAttribute("data-created-by-club");
      expect(player.className).not.toContain("border-l-4");
    });

    it("is in the weekly view too", async () => {
      resetNavigation(`${BASE}?view=week&date=2026-05-05`);
      serve({ bookings: [booking(), customerBooking()] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const customer = await screen.findByRole("button", { name: "Maria Cliente, 11:00 – 12:00, Confirmada, criada pelo clube" });
      const player = screen.getByRole("button", { name: "Rita Jogadora, 10:00 – 11:00, Confirmada" });

      expect(within(dayColumn("Terça-feira, 05/05/2026")).getByRole("button", { name: /Maria Cliente/ })).toBe(customer);
      expect(customer).toHaveTextContent("Criada pelo clube");
      expect(player).not.toHaveTextContent("Criada pelo clube");
    });

    it("stays on a customer booking that is cancelled and shown with the toggle", async () => {
      resetNavigation(`${BASE}?date=2026-05-05&cancelled=1`);
      serve({ bookings: [customerBooking({ status: "cancelled_by_club" })] });
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const box = await screen.findByRole("button", { name: "Maria Cliente, 11:00 – 12:00, Cancelada pelo clube, criada pelo clube" });

      expect(box).toHaveTextContent("Cancelada pelo clube · Criada pelo clube");
    });

    it("is in the details, with the customer's name and the phone the owner entered", async () => {
      resetNavigation(`${BASE}?date=2026-05-05&booking=${CUSTOMER_BOOKING}`);
      serve({ bookings: [customerBooking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${CUSTOMER_BOOKING}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await within(panel).findByText("Maria Cliente");

      const lines = Object.fromEntries([...panel.querySelectorAll("dt")].map((term) => [term.textContent, (term.nextElementSibling?.textContent ?? "").replace(/\s/g, " ")]));
      expect(lines).toMatchObject({ Cliente: "Maria Cliente", Telemóvel: "912345678", Origem: "Criada pelo clube", Campo: "Padel 1 · Padel", Estado: "Confirmada" });
      expect(lines).not.toHaveProperty("Jogador");
    });

    it("is not in the details of a player's booking, which has no phone", async () => {
      resetNavigation(`${BASE}?date=2026-05-05&booking=${PLAYER_BOOKING}`);
      serve({ bookings: [booking()] });
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${PLAYER_BOOKING}`, () =>
          HttpResponse.json(details({ id: PLAYER_BOOKING, bookerName: "Rita Jogadora", createdByClub: false, customerPhone: null })),
        ),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);

      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await within(panel).findByText("Rita Jogadora");

      const lines = Object.fromEntries([...panel.querySelectorAll("dt")].map((term) => [term.textContent, (term.nextElementSibling?.textContent ?? "").replace(/\s/g, " ")]));
      expect(lines).toMatchObject({ Jogador: "Rita Jogadora" });
      expect(lines).not.toHaveProperty("Telemóvel");
      expect(lines).not.toHaveProperty("Origem");
      expect(within(panel).queryByText("Criada pelo clube")).not.toBeInTheDocument();
    });
  });

  describe("cancelling a customer booking (US12)", () => {
    it("works as for a player's: a reason, and the booking is cancelled by the club and leaves the calendar", async () => {
      resetNavigation(`${BASE}?date=2026-05-05&booking=${CUSTOMER_BOOKING}`);
      const { state } = serve({ bookings: [customerBooking()] });
      const bodies: unknown[] = [];
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/bookings/${CUSTOMER_BOOKING}`, () =>
          HttpResponse.json(state.bookings[0].status === "confirmed" ? details() : details({ status: "cancelled_by_club", canCancel: false, cancellationReason: "O cliente ligou a desistir", cancelledAt: "2026-05-04T10:00:00Z" })),
        ),
        http.post(`/api/manage/clubs/${CLUB}/bookings/${CUSTOMER_BOOKING}/cancel`, async ({ request }) => {
          bodies.push(await request.json());
          state.bookings[0] = customerBooking({ status: "cancelled_by_club" });
          return HttpResponse.json(details({ status: "cancelled_by_club", canCancel: false, cancellationReason: "O cliente ligou a desistir", cancelledAt: "2026-05-04T10:00:00Z" }));
        }),
      );
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      await screen.findByRole("button", { name: /Maria Cliente/ });
      const panel = detailsPanel();
      await userEvent.click(await within(panel).findByRole("button", { name: "Cancelar reserva" }));
      await userEvent.type(screen.getByLabelText("Motivo do cancelamento"), "O cliente ligou a desistir");

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await within(panel).findByText("Reserva cancelada. O horário voltou a ficar livre.")).toBeInTheDocument();
      expect(bodies).toEqual([{ reason: "O cliente ligou a desistir" }]);
      expect(within(panel).getByText("Cancelada pelo clube")).toBeInTheDocument();
      expect(within(panel).getByText("Criada pelo clube")).toBeInTheDocument(); // still says where it came from
      await waitFor(() => expect(screen.queryByRole("button", { name: /Maria Cliente, / })).not.toBeInTheDocument());
    });

    it("needs a reason: an empty one is blocked", async () => {
      resetNavigation(`${BASE}?date=2026-05-05&booking=${CUSTOMER_BOOKING}`);
      serve({ bookings: [customerBooking()] });
      server.use(http.get(`/api/manage/clubs/${CLUB}/bookings/${CUSTOMER_BOOKING}`, () => HttpResponse.json(details())));
      renderWithProviders(<ClubCalendar clubId={CLUB} />);
      const panel = await screen.findByRole("region", { name: "Detalhes da reserva" });
      await userEvent.click(await within(panel).findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Confirmar cancelamento" }));

      expect(await screen.findByText("Indica o motivo do cancelamento.")).toBeInTheDocument();
    });
  });
});

const column = (name: string) => screen.getByRole("group", { name });
const dayColumn = (label: string) => screen.getByRole("group", { name: label });
