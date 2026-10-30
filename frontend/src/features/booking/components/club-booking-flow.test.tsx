import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Account } from "@/features/auth/api";
import type { PublicClub, PublicSlot } from "@/features/catalog/types";
import { currentPath, push, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ClubBooking } from "./club-booking";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const padel = "0199f7a0-0000-7000-8000-0000000000d1";
const tennis = "0199f7a0-0000-7000-8000-0000000000d2";
const FIRST = "2026-05-04T09:00:00Z"; // 10:00 in Lisbon (WEST)
const SECOND = "2026-05-04T10:00:00Z"; // 11:00
const THIRD = "2026-05-04T11:00:00Z"; // 12:00
const BASE = "/clubs/clube-da-foz";
const FIRST_URL = `${BASE}?pitch=${padel}&date=2026-05-04&slot=${encodeURIComponent(FIRST)}`;

const club: PublicClub = {
  slug: "clube-da-foz",
  name: "Clube da Foz",
  address: "Rua do Mar 1",
  locality: "Porto",
  phone: "912345678",
  contactEmail: "geral@foz.pt",
  pitches: [
    { id: padel, name: "Padel 1", sport: "padel", pricePerSlot: 18.5, hasPriceBands: false },
    { id: tennis, name: "Ténis 1", sport: "tennis", pricePerSlot: 12, hasPriceBands: false },
  ],
};

const account = (profile: Account["profile"]): Account => ({ id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rita Jogadora", email: "rita@exemplo.pt", profile });

const slot = (startsAt: string, available = true): PublicSlot => ({
  startsAt,
  endsAt: new Date(new Date(startsAt).getTime() + 3_600_000).toISOString().replace(".000Z", "Z"),
  price: 18.5,
  available,
});

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

type World = {
  /** Who is signed in; null for a visitor. */
  me: Account | null;
  /** The slots the API answers with, called on every request so a test can change them. */
  slots: () => PublicSlot[];
  /** Answers a booking request. */
  book?: (body: { pitchId: string; startsAt: string }) => Response;
};

/** Serves the session, the slots and bookings; returns what the page asked for. */
function serve(world: World) {
  const slotRequests: string[] = [];
  const bookings: { pitchId: string; startsAt: string }[] = [];
  const bookingBodies: unknown[] = [];
  server.use(
    http.get("/api/auth/me", () => (world.me ? HttpResponse.json(world.me) : problem(401, "unauthorized"))),
    http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
    http.get("/api/clubs/clube-da-foz/pitches/:pitchId/slots", ({ request, params }) => {
      slotRequests.push(`${String(params.pitchId)} ${new URL(request.url).searchParams.get("date")}`);
      return HttpResponse.json({ date: new URL(request.url).searchParams.get("date"), slots: world.slots() });
    }),
    http.post("/api/bookings", async ({ request }) => {
      const body = (await request.json()) as { pitchId: string; startsAt: string };
      bookingBodies.push(body);
      bookings.push(body);
      return world.book?.(body) ?? problem(500, "internal_error");
    }),
  );
  return { slotRequests, bookings, bookingBodies };
}

const booked = (startsAt: string): Response =>
  HttpResponse.json(
    { id: "0199f7a0-0000-7000-8000-0000000000b1", pitchId: padel, clubId: "c1", startsAt, endsAt: slot(startsAt).endsAt, price: "18.50", status: "confirmed" },
    { status: 201 },
  );

const slotButton = (time: string) => screen.getByRole("button", { name: new RegExp(`^${time}`) });
const summary = () => screen.queryByRole("region", { name: "Resumo da reserva" });
const confirmButton = () => screen.queryByRole("button", { name: "Confirmar reserva" });
const clean = (value: string | null) => (value ?? "").replace(/\s/g, " ");

/** The summary or the confirmation as label -> value, read from its definition list. */
function details(region: HTMLElement): Record<string, string> {
  const labels = [...region.querySelectorAll("dt")].map((term) => clean(term.textContent));
  const values = [...region.querySelectorAll("dd")].map((definition) => clean(definition.textContent));
  return Object.fromEntries(labels.map((label, index) => [label, values[index]]));
}

describe("ClubBooking: booking a slot", () => {
  beforeEach(() => {
    // 10:00 UTC on Friday 1 May 2026 is 11:00 in Lisbon.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-01T10:00:00Z"));
    resetNavigation(`${BASE}?date=2026-05-04`);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the summary", () => {
    it("opens when a free slot is chosen, with club, pitch, date, time and price, and writes the choice in the URL", async () => {
      serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND), slot(THIRD, false)] });
      renderWithProviders(<ClubBooking club={club} />);
      await screen.findByRole("list", { name: "Horários" });
      expect(summary()).not.toBeInTheDocument();

      await userEvent.click(slotButton("11:00"));

      const region = await screen.findByRole("region", { name: "Resumo da reserva" });
      expect(details(region)).toEqual({
        Clube: "Clube da Foz",
        Campo: "Padel 1",
        Data: "segunda-feira, 4 de maio",
        Hora: "11:00 – 12:00",
        Preço: "18,50 €",
      });
      expect(slotButton("11:00")).toHaveAttribute("aria-pressed", "true");
      expect(currentPath()).toBe(`${BASE}?date=2026-05-04&pitch=${padel}&slot=${encodeURIComponent(SECOND)}`);
    });

    it("changes to another slot, and closes with «Escolher outro horário»", async () => {
      serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND)] });
      renderWithProviders(<ClubBooking club={club} />);
      await screen.findByRole("list", { name: "Horários" });

      await userEvent.click(slotButton("10:00"));
      expect(details(await screen.findByRole("region", { name: "Resumo da reserva" })).Hora).toBe("10:00 – 11:00");
      await userEvent.click(slotButton("11:00"));
      await waitFor(() => expect(details(screen.getByRole("region", { name: "Resumo da reserva" })).Hora).toBe("11:00 – 12:00"));

      await userEvent.click(screen.getByRole("button", { name: "Escolher outro horário" }));

      await waitFor(() => expect(summary()).not.toBeInTheDocument());
      expect(currentPath()).not.toContain("slot=");
    });

    it("shows the shape of the slots while they load, in the same grid, so the page below does not jump (UX-020)", async () => {
      resetNavigation(FIRST_URL);
      server.use(
        http.get("/api/auth/me", () => problem(401, "unauthorized")),
        http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
        http.get("/api/clubs/clube-da-foz/pitches/:pitchId/slots", async () => {
          await delay("infinite");
          return HttpResponse.json({ date: "2026-05-04", slots: [] });
        }),
      );
      renderWithProviders(<ClubBooking club={club} />);

      const loading = await screen.findByRole("status");
      expect(loading).toHaveTextContent("A carregar os horários…");
      const blocks = loading.querySelectorAll('[data-slot="skeleton"]');
      // The two lines above the list (the date and the legend), and about a day of slots.
      expect(blocks.length).toBeGreaterThanOrEqual(14);
      expect(loading.querySelector(".grid")?.className).toContain("md:grid-cols-4");
    });

    it("keeps the main button on screen on a phone: the actions are a bar that sticks to the bottom, outside the summary card (UX-010)", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND)] });
      renderWithProviders(<ClubBooking club={club} />);

      const confirm = await screen.findByRole("button", { name: "Confirmar reserva" });

      const bar = confirm.closest('[data-slot="booking-actions"]');
      expect(bar).not.toBeNull();
      // Sticky to the bottom edge below `sm`, a plain row from `sm` up; and a sibling of the summary, not inside it, because a sticky
      // element cannot leave the box it is in.
      expect(bar?.className).toMatch(/\bsticky\b/);
      expect(bar?.className).toMatch(/\bbottom-0\b/);
      expect(bar?.className).toMatch(/\bsm:static\b/);
      expect(summary()).not.toContainElement(bar as HTMLElement);
      // What it books is repeated on the bar for the eye, and hidden from screen readers (the summary says it once).
      const line = bar?.querySelector("p[aria-hidden='true']");
      expect(line).toHaveTextContent("10:00 – 11:00 · 18,50 €");
    });

    it("makes the main actions at least 44px tall on a phone", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST)] });
      renderWithProviders(<ClubBooking club={club} />);

      for (const name of ["Confirmar reserva", "Escolher outro horário"]) {
        expect((await screen.findByRole("button", { name })).className).toContain("h-11");
      }
    });

    it("gives a visitor the sign-in button in the same bar", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: null, slots: () => [slot(FIRST)] });
      renderWithProviders(<ClubBooking club={club} />);

      const signIn = await screen.findByRole("link", { name: "Entrar para reservar" });

      expect(signIn.closest('[data-slot="booking-actions"]')).not.toBeNull();
      expect(signIn.className).toContain("h-11");
    });

    it("closes when the pitch or the date changes, because the slot belongs to them", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND)] });
      renderWithProviders(<ClubBooking club={club} />);
      await screen.findByRole("region", { name: "Resumo da reserva" });

      await userEvent.selectOptions(screen.getByLabelText("Campo"), tennis);

      await waitFor(() => expect(summary()).not.toBeInTheDocument());
      expect(currentPath()).toBe(`${BASE}?pitch=${tennis}&date=2026-05-04`);
    });
  });

  describe("who can book", () => {
    it("sends a visitor to sign in, coming back to this club, pitch, date and slot", async () => {
      serve({ me: null, slots: () => [slot(FIRST), slot(SECOND)] });
      renderWithProviders(<ClubBooking club={club} />);
      await screen.findByRole("list", { name: "Horários" });

      await userEvent.click(slotButton("11:00"));

      const link = await screen.findByRole("link", { name: "Entrar para reservar" });
      const next = new URL(link.getAttribute("href") ?? "", "http://localhost").searchParams.get("next");
      expect(link.getAttribute("href")).toMatch(/^\/login\?next=/);
      expect(next).toBe(`${BASE}?date=2026-05-04&pitch=${padel}&slot=${encodeURIComponent(SECOND)}`);
      expect(confirmButton()).not.toBeInTheDocument();
    });

    it("lets a player confirm, and gives owners and admins no booking button at all", async () => {
      for (const profile of ["Player", "Owner", "Admin"] as const) {
        resetNavigation(FIRST_URL);
        serve({ me: account(profile), slots: () => [slot(FIRST), slot(SECOND)] });
        const { unmount } = renderWithProviders(<ClubBooking club={club} />);
        await screen.findByRole("list", { name: "Horários" });

        if (profile === "Player") {
          expect(await screen.findByRole("button", { name: "Confirmar reserva" })).toBeEnabled();
        } else {
          // The slots are shown to look at, not to pick: no buttons, no summary, no way in, and a word on why.
          await screen.findByText(/Só os jogadores podem fazer reservas/);
          expect(screen.queryByRole("button", { name: /reservar/i })).not.toBeInTheDocument();
          expect(screen.queryByRole("link", { name: /reservar/i })).not.toBeInTheDocument();
          expect(screen.queryByRole("button", { name: /^1\d:00/ })).not.toBeInTheDocument();
          expect(summary(), profile).not.toBeInTheDocument();
        }
        unmount();
      }
    });

    it("waits for the session before offering to confirm", async () => {
      resetNavigation(FIRST_URL);
      server.use(
        http.get("/api/auth/me", () => new Promise<Response>(() => undefined)), // never answers
        http.get("/api/clubs/clube-da-foz/pitches/:pitchId/slots", () => HttpResponse.json({ date: "2026-05-04", slots: [slot(FIRST)] })),
      );
      renderWithProviders(<ClubBooking club={club} />);

      expect(await screen.findByRole("button", { name: "A verificar a sessão…" })).toBeDisabled();
      expect(confirmButton()).not.toBeInTheDocument();
    });
  });

  describe("confirming", () => {
    it("books the slot with only the pitch and the start as an instant with its zone, and shows the confirmation", async () => {
      resetNavigation(FIRST_URL);
      let isBooked = false;
      const { bookings, slotRequests } = serve({
        me: account("Player"),
        slots: () => [slot(FIRST, !isBooked), slot(SECOND)],
        book: (body) => {
          isBooked = true;
          return booked(body.startsAt);
        },
      });
      const { queryClient } = renderWithProviders(<ClubBooking club={club} />);
      const invalidate = vi.spyOn(queryClient, "invalidateQueries");
      await userEvent.click(await screen.findByRole("button", { name: "Confirmar reserva" }));

      const confirmation = await screen.findByRole("region", { name: "Reserva confirmada" });
      // UX-002: the confirm button is gone, so focus goes to the heading; the section is not also a live region (read twice).
      expect(within(confirmation).getByRole("heading", { name: "Reserva confirmada" })).toHaveFocus();
      expect(confirmation).not.toHaveAttribute("role");
      // What happens next is said: the email, and where to cancel.
      expect(within(confirmation).getByText(/Enviámos-te um email de confirmação\. Podes cancelar a reserva em «As minhas reservas»/)).toBeInTheDocument();
      // US19: the confirmation leads to "As minhas reservas", and the new booking makes that list stale.
      expect(within(confirmation).getByRole("link", { name: "Ver as minhas reservas" })).toHaveAttribute("href", "/bookings");
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["my-bookings"] });
      expect(bookings).toEqual([{ pitchId: padel, startsAt: FIRST }]);
      expect(Object.keys(bookings[0])).toEqual(["pitchId", "startsAt"]);
      expect(details(confirmation)).toEqual({
        Clube: "Clube da Foz",
        Campo: "Padel 1",
        Data: "segunda-feira, 4 de maio",
        Hora: "10:00 – 11:00",
        Preço: "18,50 €",
      });
      // The summary is done, the choice is gone from the URL, and the slots are loaded again: that slot is taken now.
      expect(summary()).not.toBeInTheDocument();
      expect(currentPath()).not.toContain("slot=");
      await waitFor(() => expect(slotRequests.length).toBeGreaterThan(1));
      await waitFor(() => expect(slotButton("10:00")).toBeDisabled());
    });

    it("cannot be submitted twice while the booking is being made", async () => {
      resetNavigation(FIRST_URL);
      let release: (() => void) | undefined;
      const { bookings } = serve({
        me: account("Player"),
        slots: () => [slot(FIRST)],
        book: () => booked(FIRST),
      });
      server.use(
        http.post("/api/bookings", async ({ request }) => {
          bookings.push((await request.json()) as { pitchId: string; startsAt: string });
          await new Promise<void>((resolve) => {
            release = resolve;
          });
          return booked(FIRST);
        }),
      );
      renderWithProviders(<ClubBooking club={club} />);
      const button = await screen.findByRole("button", { name: "Confirmar reserva" });

      await userEvent.click(button);

      expect(await screen.findByRole("button", { name: "A reservar…" })).toBeDisabled();
      expect(screen.getByRole("button", { name: "Escolher outro horário" })).toBeDisabled();
      release?.();
      await screen.findByRole("region", { name: "Reserva confirmada" });
      expect(bookings).toHaveLength(1);
    });
  });

  describe("when the slot cannot be booked", () => {
    it("on 409 says it was just booked, reloads the slots and closes the summary", async () => {
      resetNavigation(FIRST_URL);
      let taken = false;
      const { slotRequests } = serve({
        me: account("Player"),
        slots: () => [slot(FIRST, !taken), slot(SECOND)],
        book: () => {
          taken = true; // someone else got it first
          return problem(409, "slot_taken");
        },
      });
      renderWithProviders(<ClubBooking club={club} />);
      await userEvent.click(await screen.findByRole("button", { name: "Confirmar reserva" }));

      // The refusal replaces the confirm button, so it takes focus (UX-002) instead of leaving it on <body>.
      const alert = await screen.findByText("Este horário acabou de ser reservado. Escolhe outro.");
      expect(alert).toHaveFocus();
      expect(screen.queryByRole("region", { name: "Reserva confirmada" })).not.toBeInTheDocument();
      expect(summary()).not.toBeInTheDocument();
      expect(currentPath()).not.toContain("slot=");
      // The list was asked for again and now shows that slot disabled, with the other still free.
      await waitFor(() => expect(slotRequests).toHaveLength(2));
      await waitFor(() => expect(slotButton("10:00")).toBeDisabled());
      expect(slotButton("11:00")).toBeEnabled();
    });

    it.each([
      [409, "slot_blocked", "Este horário não está disponível. Escolhe outro."],
      [422, "slot_started", "Este horário já começou. Escolhe outro."],
      [422, "invalid_slot", "Este horário já não existe. Escolhe outro."],
      [422, "pitch_inactive", "Este campo já não está a aceitar reservas."],
      [422, "club_unavailable", "Este clube já não está a aceitar reservas."],
    ])("on %i %s shows its own message, reloads the slots and closes the summary", async (status, code, message) => {
      resetNavigation(FIRST_URL);
      const { slotRequests } = serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND)], book: () => problem(status, code) });
      renderWithProviders(<ClubBooking club={club} />);
      await userEvent.click(await screen.findByRole("button", { name: "Confirmar reserva" }));

      expect(await screen.findByText(message)).toHaveFocus();
      expect(summary()).not.toBeInTheDocument();
      await waitFor(() => expect(slotRequests).toHaveLength(2));
    });

    it("keeps the summary when the server cannot be reached, so the player can try again", async () => {
      resetNavigation(FIRST_URL);
      const { slotRequests } = serve({ me: account("Player"), slots: () => [slot(FIRST)] });
      server.use(http.post("/api/bookings", () => HttpResponse.error()));
      renderWithProviders(<ClubBooking club={club} />);
      await userEvent.click(await screen.findByRole("button", { name: "Confirmar reserva" }));

      expect(await screen.findByText(/Não foi possível contactar o servidor\./)).toBeInTheDocument();
      expect(summary()).toBeInTheDocument();
      expect(confirmButton()).toBeEnabled();
      expect(slotRequests).toHaveLength(1);
    });

    it("sends the player to sign in again, back to this slot, if the session ended", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST)], book: () => problem(401, "unauthorized") });
      renderWithProviders(<ClubBooking club={club} />);
      await userEvent.click(await screen.findByRole("button", { name: "Confirmar reserva" }));

      await waitFor(() => expect(push).toHaveBeenCalledWith(`/login?next=${encodeURIComponent(FIRST_URL)}`, undefined));
    });

    it("clears the message when another slot is chosen", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND)], book: () => problem(409, "slot_taken") });
      renderWithProviders(<ClubBooking club={club} />);
      await userEvent.click(await screen.findByRole("button", { name: "Confirmar reserva" }));
      await screen.findByText("Este horário acabou de ser reservado. Escolhe outro.");

      await userEvent.click(slotButton("11:00"));

      await waitFor(() => expect(screen.queryByText("Este horário acabou de ser reservado. Escolhe outro.")).not.toBeInTheDocument());
    });
  });

  describe("arriving at a slot from a link or after signing in", () => {
    it("opens the summary of that slot for a player straight away", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST), slot(SECOND)] });

      renderWithProviders(<ClubBooking club={club} />);

      const region = await screen.findByRole("region", { name: "Resumo da reserva" });
      expect(details(region).Hora).toBe("10:00 – 11:00");
      expect(screen.getByLabelText("Campo")).toHaveValue(padel);
      expect(screen.getByLabelText("Data")).toHaveValue("2026-05-04");
      expect(slotButton("10:00")).toHaveAttribute("aria-pressed", "true");
    });

    it("says the slot is gone, with no summary, if it was taken meanwhile", async () => {
      resetNavigation(FIRST_URL);
      serve({ me: account("Player"), slots: () => [slot(FIRST, false), slot(SECOND)] });

      renderWithProviders(<ClubBooking club={club} />);

      expect(await screen.findByRole("alert")).toHaveTextContent("Este horário já não está disponível. Escolhe outro.");
      expect(summary()).not.toBeInTheDocument();
      expect(slotButton("10:00")).toBeDisabled();
    });

    it("tells the same story in the same words for a slot that is not on the list at all", async () => {
      resetNavigation(`${BASE}?pitch=${padel}&date=2026-05-04&slot=${encodeURIComponent("2026-05-04T03:00:00Z")}`);
      serve({ me: account("Player"), slots: () => [slot(FIRST)] });

      renderWithProviders(<ClubBooking club={club} />);

      expect(await screen.findByRole("alert")).toHaveTextContent("Este horário já não está disponível. Escolhe outro.");
    });
  });

  it("shows a free slot to a visitor only as a choice, and unavailable ones disabled with no reason", async () => {
    serve({ me: null, slots: () => [slot(FIRST), slot(SECOND, false)] });
    renderWithProviders(<ClubBooking club={club} />);

    await screen.findByRole("list", { name: "Horários" });

    const items = within(screen.getByRole("list", { name: "Horários" })).getAllByRole("button");
    expect(items.map((item) => item.hasAttribute("disabled"))).toEqual([false, true]);
    expect(screen.queryByText(/reservad|bloquead|fechad|começ/i)).not.toBeInTheDocument();
  });
});
