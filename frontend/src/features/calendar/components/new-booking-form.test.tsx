import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { CalendarPitch, ClubBooking } from "../types";
import { NewBookingForm, type NewBookingPrefill } from "./new-booking-form";

const CLUB = "0199f7a0-0000-7000-8000-0000000000c1";
const PADEL = "0199f7a0-0000-7000-8000-0000000000d1";
const TENNIS = "0199f7a0-0000-7000-8000-0000000000d2";
const NEW_ID = "0199f7a0-0000-7000-8000-0000000000e1";

const pitch = (id: string, name: string, sport: string): CalendarPitch => ({ id, name, sport, status: "active", slotDurationMinutes: 60, days: [] });
const pitches = [pitch(PADEL, "Padel 1", "padel"), pitch(TENNIS, "Ténis 1", "tennis")];

// Monday 4 May 2026, 10:00 UTC = 11:00 in Lisbon. Today's slots (WEST, UTC+1): 09:00 08:00Z, 10:00 09:00Z, 11:00 10:00Z, 12:00 11:00Z.
const TODAY = "2026-05-04";
const TOMORROW = "2026-05-05";
const slot = (startsAt: string, patch: { blocked?: boolean; booked?: boolean } = {}) => ({
  startsAt,
  endsAt: new Date(new Date(startsAt).getTime() + 3_600_000).toISOString().replace(".000Z", "Z"),
  blocked: false,
  booked: false,
  ...patch,
});

const created = (patch: Partial<ClubBooking> = {}): ClubBooking => ({
  id: NEW_ID,
  pitchId: PADEL,
  pitchName: "Padel 1",
  sport: "padel",
  startsAt: "2026-05-05T09:00:00Z",
  endsAt: "2026-05-05T10:00:00Z",
  price: 18.5,
  status: "confirmed",
  playerName: "Maria Cliente",
  createdByClub: true,
  customerPhone: "912345678",
  createdAt: "2026-05-04T10:00:00Z",
  cancelledAt: null,
  cancellationReason: null,
  canCancel: true,
  ...patch,
});

function problem(status: number, code: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...extra }, { status, headers: { "Content-Type": "application/problem+json" } });
}

type Slots = ReturnType<typeof slot>[];

/** Serves the owner's slot preview, one list per pitch and date; returns the (pitch, date) pairs asked for. */
function serveSlots(lists: (pitchId: string, date: string) => Slots) {
  const asked: string[] = [];
  server.use(
    http.get(`/api/manage/clubs/${CLUB}/pitches/:pitchId/slots`, ({ params, request }) => {
      const date = new URL(request.url).searchParams.get("date") ?? "";
      asked.push(`${String(params.pitchId)} ${date}`);
      const slots = lists(String(params.pitchId), date);
      return HttpResponse.json({ date, slotDurationMinutes: 60, slots });
    }),
  );
  return asked;
}

const tomorrowSlots = (): Slots => [slot("2026-05-05T08:00:00Z"), slot("2026-05-05T09:00:00Z"), slot("2026-05-05T10:00:00Z", { booked: true }), slot("2026-05-05T11:00:00Z", { blocked: true })];

type Setup = { prefill?: NewBookingPrefill; onClose?: () => void; onCreated?: (booking: ClubBooking) => void };

function renderForm({ prefill = { pitchId: PADEL, date: TOMORROW }, onClose = vi.fn(), onCreated = vi.fn() }: Setup = {}) {
  return { onClose: onClose as Mock<() => void>, onCreated: onCreated as Mock<(booking: ClubBooking) => void>, ...renderWithProviders(<NewBookingForm clubId={CLUB} pitches={pitches} prefill={prefill} onClose={onClose} onCreated={onCreated} />) };
}

const slotButton = (label: string | RegExp) => screen.getByRole("button", { name: label });
const nameField = () => screen.getByLabelText("Nome do cliente");
const phoneField = () => screen.getByLabelText("Telemóvel do cliente");
const submit = () => userEvent.click(screen.getByRole("button", { name: "Criar reserva" }));

describe("NewBookingForm", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-04T10:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the free slots", () => {
    it("lists the pitch's slots for the date in Lisbon time, and only the free ones can be chosen", async () => {
      serveSlots(tomorrowSlots);
      renderForm();

      await screen.findByRole("button", { name: /^09:00 – 10:00/ });

      expect(slotButton("09:00 – 10:00")).toBeEnabled();
      expect(slotButton("10:00 – 11:00")).toBeEnabled();
      expect(slotButton("11:00 – 12:00 Reservado")).toBeDisabled();
      expect(slotButton("12:00 – 13:00 Bloqueado")).toBeDisabled();
      expect(screen.getByLabelText("Data")).toHaveValue(TOMORROW);
      expect((screen.getByLabelText("Campo") as HTMLSelectElement).value).toBe(PADEL);
    });

    it("does not offer a slot that has started today, nor starting now", async () => {
      serveSlots((_pitch, date) =>
        date === TODAY
          ? [slot("2026-05-04T08:00:00Z"), slot("2026-05-04T09:00:00Z"), slot("2026-05-04T10:00:00Z"), slot("2026-05-04T11:00:00Z")]
          : [],
      );
      renderForm({ prefill: { pitchId: PADEL, date: TODAY } });

      await screen.findByRole("button", { name: /^12:00 – 13:00/ });

      expect(slotButton("09:00 – 10:00 Já começou")).toBeDisabled();
      expect(slotButton("10:00 – 11:00 Já começou")).toBeDisabled();
      expect(slotButton("11:00 – 12:00 Já começou")).toBeDisabled(); // starting now counts as started
      expect(slotButton("12:00 – 13:00")).toBeEnabled();
    });

    it("marks a slot taken by a booking and blocked at the same time with the booking first, and neither can be chosen", async () => {
      serveSlots(() => [slot("2026-05-05T08:00:00Z", { booked: true, blocked: true })]);
      renderForm();

      expect(await screen.findByRole("button", { name: /Reservado/ })).toBeDisabled();
    });

    it("chooses a slot, shows it as chosen, and changes the choice", async () => {
      serveSlots(tomorrowSlots);
      renderForm();
      await screen.findByRole("button", { name: /^09:00 – 10:00/ });

      await userEvent.click(slotButton("09:00 – 10:00"));
      expect(slotButton("09:00 – 10:00")).toHaveAttribute("aria-pressed", "true");
      await userEvent.click(slotButton("10:00 – 11:00"));

      expect(slotButton("09:00 – 10:00")).toHaveAttribute("aria-pressed", "false");
      expect(slotButton("10:00 – 11:00")).toHaveAttribute("aria-pressed", "true");
    });

    it("clicking a slot that is not free does nothing", async () => {
      serveSlots(tomorrowSlots);
      renderForm();
      await screen.findByRole("button", { name: /^09:00 – 10:00/ });

      await userEvent.click(slotButton("11:00 – 12:00 Reservado"));

      expect(slotButton("11:00 – 12:00 Reservado")).toHaveAttribute("aria-pressed", "false");
    });

    it("says when the pitch has no slots that day", async () => {
      serveSlots(() => []);
      renderForm();

      expect(await screen.findByText("Sem horários neste dia: o campo está fechado.")).toBeInTheDocument();
    });

    it("shows an error with a way to try again when the slots cannot be read", async () => {
      let failing = true;
      server.use(
        http.get(`/api/manage/clubs/${CLUB}/pitches/:pitchId/slots`, () =>
          failing ? problem(500, "internal_error") : HttpResponse.json({ date: TOMORROW, slotDurationMinutes: 60, slots: [slot("2026-05-05T08:00:00Z")] }),
        ),
      );
      renderForm();

      expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      failing = false;
      await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

      expect(await screen.findByRole("button", { name: /^09:00 – 10:00/ })).toBeEnabled();
    });

    it("loads the slots again for another pitch or date, and forgets the chosen slot", async () => {
      const asked = serveSlots((pitchId, date) => (pitchId === TENNIS ? [slot(`${date}T14:00:00Z`)] : tomorrowSlots()));
      renderForm();
      await userEvent.click(await screen.findByRole("button", { name: /^09:00 – 10:00/ }));

      await userEvent.selectOptions(screen.getByLabelText("Campo"), TENNIS);

      expect(await screen.findByRole("button", { name: "15:00 – 16:00" })).toHaveAttribute("aria-pressed", "false");
      expect(asked).toContain(`${TENNIS} ${TOMORROW}`);
      await userEvent.click(slotButton("15:00 – 16:00"));
      fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-05-07" } });
      await waitFor(() => expect(asked).toContain(`${TENNIS} 2026-05-07`));
      expect(document.querySelector('[aria-pressed="true"]')).toBeNull();
    });

    it("falls back to today for a date outside today to 30 days ahead, and never asks the API for the other", async () => {
      const asked = serveSlots(tomorrowSlots);
      renderForm({ prefill: { pitchId: PADEL, date: "2026-06-30" } }); // 57 days ahead

      await waitFor(() => expect(asked.length).toBeGreaterThan(0));

      expect(screen.getByLabelText("Data")).toHaveValue(TODAY);
      expect(asked.every((entry) => entry.endsWith(TODAY))).toBe(true);
    });

    it("says so and asks for nothing when the date is typed outside the window", async () => {
      const asked = serveSlots(tomorrowSlots);
      renderForm();
      await screen.findByRole("button", { name: /^09:00 – 10:00/ });
      const askedBefore = asked.length;

      fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-07-01" } });

      expect(await screen.findByText("Escolhe uma data de hoje até 30 dias à frente.")).toBeInTheDocument();
      expect(asked.length).toBe(askedBefore);
    });

    it("offers only the pitches it was given (the active ones), the first by default", async () => {
      serveSlots(tomorrowSlots);
      renderForm({ prefill: {} });
      await screen.findByRole("button", { name: /^09:00 – 10:00/ });

      const select = screen.getByLabelText("Campo") as HTMLSelectElement;

      expect([...select.options].map((option) => option.textContent)).toEqual(["Padel 1 · Padel", "Ténis 1 · Ténis"]);
      expect(select.value).toBe(PADEL);
      expect(screen.getByLabelText("Data")).toHaveValue(TODAY);
    });
  });

  describe("the time the owner clicked", () => {
    it("chooses the free slot that contains it", async () => {
      serveSlots(tomorrowSlots);
      renderForm({ prefill: { pitchId: PADEL, date: TOMORROW, startsAt: "2026-05-05T09:00:00Z" } }); // the 10:00 hour

      await screen.findByRole("button", { name: /^10:00 – 11:00/ });

      await waitFor(() => expect(slotButton("10:00 – 11:00")).toHaveAttribute("aria-pressed", "true"));
      expect(slotButton("09:00 – 10:00")).toHaveAttribute("aria-pressed", "false");
    });

    it("chooses nothing when that slot is not free", async () => {
      serveSlots(tomorrowSlots);
      renderForm({ prefill: { pitchId: PADEL, date: TOMORROW, startsAt: "2026-05-05T10:00:00Z" } }); // the booked 11:00 hour

      await screen.findByRole("button", { name: /^11:00 – 12:00/ });

      expect(document.querySelector('[aria-pressed="true"]')).toBeNull();
    });

    it("chooses nothing for a time that has already started", async () => {
      serveSlots(() => [slot("2026-05-04T08:00:00Z"), slot("2026-05-04T11:00:00Z")]);
      renderForm({ prefill: { pitchId: PADEL, date: TODAY, startsAt: "2026-05-04T08:00:00Z" } });

      await screen.findByRole("button", { name: /^09:00 – 10:00/ });

      expect(document.querySelector('[aria-pressed="true"]')).toBeNull();
    });
  });

  describe("validation", () => {
    it("asks for the slot, the name and the phone, and sends nothing", async () => {
      serveSlots(tomorrowSlots);
      let posted = 0;
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, () => {
        posted += 1;
        return HttpResponse.json(created(), { status: 201 });
      }));
      renderForm();
      await screen.findByRole("button", { name: /^09:00 – 10:00/ });

      await submit();

      expect(await screen.findByText("Escolhe um horário livre.")).toBeInTheDocument();
      expect(screen.getByText("Indica o nome do cliente.")).toBeInTheDocument();
      expect(screen.getByText("Indica o telemóvel do cliente.")).toBeInTheDocument();
      expect(nameField()).toHaveAttribute("aria-invalid", "true");
      expect(phoneField()).toHaveAttribute("aria-invalid", "true");
      expect(posted).toBe(0);
    });

    it.each(["abc", "12345678", "1234567890123456", "912-345-678", "++351912345678"])("rejects the phone «%s»", async (phone) => {
      serveSlots(tomorrowSlots);
      renderForm();
      await userEvent.click(await screen.findByRole("button", { name: /^09:00 – 10:00/ }));
      await userEvent.type(nameField(), "Maria Cliente");
      await userEvent.type(phoneField(), phone);

      await submit();

      expect(await screen.findByText("Indica um número de telemóvel válido, com 9 a 15 algarismos.")).toBeInTheDocument();
    });

    it("rejects a name of more than 100 characters and accepts exactly 100", async () => {
      serveSlots(tomorrowSlots);
      const bodies: { customerName: string }[] = [];
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, async ({ request }) => {
        bodies.push((await request.json()) as { customerName: string });
        return HttpResponse.json(created(), { status: 201 });
      }));
      renderForm();
      await userEvent.click(await screen.findByRole("button", { name: /^09:00 – 10:00/ }));
      await userEvent.click(nameField());
      await userEvent.paste("a".repeat(101));
      await userEvent.type(phoneField(), "912345678");

      await submit();

      expect(await screen.findByText("O nome pode ter no máximo 100 caracteres.")).toBeInTheDocument();
      expect(bodies).toEqual([]);
      await userEvent.clear(nameField());
      await userEvent.click(nameField());
      await userEvent.paste("b".repeat(100));
      await submit();

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0].customerName).toHaveLength(100);
    });

    it("takes the spaces out of the phone and trims the name, as the API does", async () => {
      serveSlots(tomorrowSlots);
      const bodies: unknown[] = [];
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(created(), { status: 201 });
      }));
      renderForm();
      await userEvent.click(await screen.findByRole("button", { name: /^10:00 – 11:00/ }));
      await userEvent.type(nameField(), "  Maria Cliente  ");
      await userEvent.type(phoneField(), "+351 912 345 678");

      await submit();

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toEqual({ pitchId: PADEL, startsAt: "2026-05-05T09:00:00Z", customerName: "Maria Cliente", customerPhone: "+351912345678" });
    });

    it("puts the API's validation messages on their fields", async () => {
      serveSlots(tomorrowSlots);
      server.use(
        http.post(`/api/manage/clubs/${CLUB}/bookings`, () =>
          problem(400, "validation_failed", { errors: { customerName: ["Indica o nome do cliente."], customerPhone: ["Indica um número de telemóvel válido, com 9 a 15 algarismos."] } }),
        ),
      );
      renderForm();
      await userEvent.click(await screen.findByRole("button", { name: /^09:00 – 10:00/ }));
      await userEvent.type(nameField(), "Maria");
      await userEvent.type(phoneField(), "912345678");

      await submit();

      expect(await screen.findByText("Indica o nome do cliente.")).toBeInTheDocument();
      expect(screen.getByText("Indica um número de telemóvel válido, com 9 a 15 algarismos.")).toBeInTheDocument();
    });
  });

  describe("creating", () => {
    async function fillIn() {
      await userEvent.click(await screen.findByRole("button", { name: /^09:00 – 10:00/ }));
      await userEvent.type(nameField(), "Maria Cliente");
      await userEvent.type(phoneField(), "912345678");
    }

    it("books the slot for the customer, says nothing about email, and hands the booking over", async () => {
      serveSlots(tomorrowSlots);
      const bodies: unknown[] = [];
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(created(), { status: 201 });
      }));
      const { onCreated, queryClient } = renderForm();
      const invalidate = vi.spyOn(queryClient, "invalidateQueries");
      expect(screen.getByText("O cliente não tem conta, por isso não recebe nenhum email.")).toBeInTheDocument();
      await fillIn();

      await submit();

      await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(1));
      expect(bodies).toEqual([{ pitchId: PADEL, startsAt: "2026-05-05T08:00:00Z", customerName: "Maria Cliente", customerPhone: "912345678" }]);
      expect(onCreated.mock.calls[0][0]).toMatchObject({ id: NEW_ID, playerName: "Maria Cliente", createdByClub: true });
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["calendar", CLUB], refetchType: "all" });
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["pitches", CLUB, PADEL, "slots"], refetchType: "all" });
    });

    it("cannot be sent twice while it is being made", async () => {
      serveSlots(tomorrowSlots);
      let release: (() => void) | undefined;
      let posts = 0;
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, async () => {
        posts += 1;
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return HttpResponse.json(created(), { status: 201 });
      }));
      renderForm();
      await fillIn();
      await submit();

      const working = await screen.findByRole("button", { name: "A reservar…" });

      expect(working).toBeDisabled();
      await userEvent.click(working);
      expect(posts).toBe(1);
      release?.();
    });

    it.each([
      ["slot_taken", 409, "Este horário acabou de ser reservado. Escolhe outro."],
      ["slot_blocked", 409, "Este horário não está disponível. Escolhe outro."],
    ])("shows the message for %s, clears the choice and loads the slots again, keeping the customer's data", async (code, status, message) => {
      let taken = false;
      const asked = serveSlots(() => (taken ? [slot("2026-05-05T08:00:00Z", { booked: true }), slot("2026-05-05T09:00:00Z")] : [slot("2026-05-05T08:00:00Z"), slot("2026-05-05T09:00:00Z")]));
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, () => {
        taken = true;
        return problem(status, code);
      }));
      const { onCreated } = renderForm();
      await fillIn();
      const askedBefore = asked.length;

      await submit();

      expect(await screen.findByRole("alert")).toHaveTextContent(message);
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
      expect(await screen.findByRole("button", { name: "09:00 – 10:00 Reservado" })).toBeDisabled();
      expect(document.querySelector('[aria-pressed="true"]')).toBeNull();
      expect(nameField()).toHaveValue("Maria Cliente");
      expect(phoneField()).toHaveValue("912345678");
      expect(onCreated).not.toHaveBeenCalled();
    });

    it("lets the owner choose another slot after a conflict, and that one is booked", async () => {
      let first = true;
      serveSlots(() => [slot("2026-05-05T08:00:00Z"), slot("2026-05-05T09:00:00Z")]);
      const bodies: { startsAt: string }[] = [];
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, async ({ request }) => {
        bodies.push((await request.json()) as { startsAt: string });
        if (first) {
          first = false;
          return problem(409, "slot_taken");
        }
        return HttpResponse.json(created(), { status: 201 });
      }));
      const { onCreated } = renderForm();
      await fillIn();
      await submit();
      await screen.findByRole("alert");

      await userEvent.click(slotButton("10:00 – 11:00"));
      await submit();

      await waitFor(() => expect(onCreated).toHaveBeenCalled());
      expect(bodies.map((body) => body.startsAt)).toEqual(["2026-05-05T08:00:00Z", "2026-05-05T09:00:00Z"]);
    });

    it.each([
      ["slot_started", 422, "Este horário já começou. Escolhe outro."],
      ["invalid_slot", 422, "Este horário já não existe. Escolhe outro."],
      ["outside_booking_window", 422, "Só é possível reservar de hoje até 30 dias à frente."],
      ["pitch_inactive", 422, "Este campo já não está a aceitar reservas."],
      ["pitch_not_found", 404, "Este campo já não existe neste clube."],
    ])("shows the message for %s and loads the slots again", async (code, status, message) => {
      const asked = serveSlots(tomorrowSlots);
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, () => problem(status, code)));
      renderForm();
      await fillIn();
      const askedBefore = asked.length;

      await submit();

      expect(await screen.findByRole("alert")).toHaveTextContent(message);
      await waitFor(() => expect(asked.length).toBeGreaterThan(askedBefore));
    });

    it("shows the message when the club no longer takes bookings", async () => {
      serveSlots(tomorrowSlots);
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, () => problem(422, "club_unavailable")));
      renderForm();
      await fillIn();

      await submit();

      expect(await screen.findByText("Este clube já não está a aceitar reservas.")).toBeInTheDocument();
    });

    it("shows a toast for a failure that is not the API's and keeps the form as it is", async () => {
      serveSlots(tomorrowSlots);
      server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, () => problem(500, "internal_error")));
      const { onCreated } = renderForm();
      await fillIn();

      await submit();

      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
      expect(slotButton("09:00 – 10:00")).toHaveAttribute("aria-pressed", "true");
      expect(screen.getByRole("button", { name: "Criar reserva" })).toBeEnabled();
      expect(onCreated).not.toHaveBeenCalled();
    });
  });

  it("closes with «Fechar» and with «Cancelar», sending nothing", async () => {
    serveSlots(tomorrowSlots);
    let posted = 0;
    server.use(http.post(`/api/manage/clubs/${CLUB}/bookings`, () => {
      posted += 1;
      return HttpResponse.json(created(), { status: 201 });
    }));
    const { onClose } = renderForm();
    await screen.findByRole("button", { name: /^09:00 – 10:00/ });

    await userEvent.click(within(screen.getByRole("form", { name: "Nova reserva" })).getByRole("button", { name: "Fechar" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(onClose).toHaveBeenCalledTimes(2);
    expect(posted).toBe(0);
  });
});
