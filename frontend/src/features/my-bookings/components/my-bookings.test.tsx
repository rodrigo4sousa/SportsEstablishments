import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { MyBooking } from "../types";
import { MyBookings } from "./my-bookings";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const FIRST_ID = "0199f7a0-0000-7000-8000-0000000000b1";
const SECOND_ID = "0199f7a0-0000-7000-8000-0000000000b2";

/** 17:00 UTC on Saturday 4 July 2026 is 18:00 in Lisbon (WEST); the deadline 24 h earlier is 18:00 on the 3rd. */
const booking = (patch: Partial<MyBooking> = {}): MyBooking => ({
  id: FIRST_ID,
  clubName: "Clube da Foz",
  clubSlug: "clube-da-foz",
  pitchName: "Padel 1",
  sport: "padel",
  startsAt: "2026-07-04T17:00:00Z",
  endsAt: "2026-07-04T18:00:00Z",
  price: 18.5,
  series: null,
  status: "confirmed",
  canCancel: true,
  cancellationDeadline: "2026-07-03T17:00:00Z",
  ...patch,
});

const pastBooking = (patch: Partial<MyBooking> = {}): MyBooking =>
  booking({ status: "confirmed", canCancel: false, cancellationDeadline: "2026-06-03T17:00:00Z", startsAt: "2026-06-04T17:00:00Z", endsAt: "2026-06-04T18:00:00Z", ...patch });

function page(items: MyBooking[], extra: { page?: number; totalCount?: number } = {}) {
  return HttpResponse.json({ items, page: extra.page ?? 1, pageSize: 20, totalCount: extra.totalCount ?? items.length });
}

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

/** Serves the list; returns the queries the API received. */
function serveList(respond: (query: URLSearchParams) => Response) {
  const asked: URLSearchParams[] = [];
  server.use(
    http.get("/api/me/bookings", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.push(query);
      return respond(query);
    }),
  );
  return asked;
}

const panel = () => screen.getByRole("region", { name: /^Reservas (próximas|anteriores)$/ });
const items = () => within(panel()).getAllByRole("listitem");
const tab = (name: string) => screen.getByRole("link", { name });

/** What one compact row says: when, and the line under it (pitch, sport, price, series). The day is the heading of its group. */
function row(item: HTMLElement): { time: string; details: string } {
  const squash = (text: string | null | undefined) => (text ?? "").replace(/\s/g, " ");
  return { time: squash(item.querySelector('[data-slot="booking-time"]')?.textContent), details: squash(item.querySelector('[data-slot="booking-details"]')?.textContent) };
}

describe("MyBookings", () => {
  beforeEach(() => {
    resetNavigation("/bookings");
    // Only the date is fake (30 June 2026, noon in Lisbon), so the day headings do not depend on when the suite runs.
    vi.useFakeTimers({ toFake: ["Date"], now: new Date("2026-06-30T11:00:00Z") });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  describe("the list", () => {
    it("opens on «Próximas» and shows club, pitch, time, price and status of each booking, under the heading of its day (UX-022)", async () => {
      const asked = serveList(() => page([booking()]));

      renderWithProviders(<MyBookings />);

      const [item] = await waitFor(() => {
        const found = items();
        expect(found).toHaveLength(1);
        return found;
      });
      expect(asked[0].get("scope")).toBe("upcoming");
      expect(asked[0].has("page")).toBe(false);
      expect(tab("Próximas")).toHaveAttribute("aria-current", "page");
      expect(tab("Anteriores")).not.toHaveAttribute("aria-current");
      expect(within(item).getByRole("link", { name: "Clube da Foz" })).toHaveAttribute("href", "/clubs/clube-da-foz");
      expect(row(item)).toEqual({ time: "18:00 – 19:00", details: "Padel 1 · Padel · 18,50 €" });
      // The day is said once, as the heading of the list; the row does not repeat it, nor the numeric date.
      expect(screen.getByRole("heading", { level: 3, name: "sábado, 4 de julho" })).toBeInTheDocument();
      expect(within(item).queryByText(/julho|04\/07/)).not.toBeInTheDocument();
      expect(within(item).getByText("Confirmada")).toBeInTheDocument();
      expect(screen.getByText("1 reserva")).toBeInTheDocument();
    });

    it("heads the day «Hoje» or «Amanhã» and groups the bookings of the same day in one list (UX-022)", async () => {
      serveList(() =>
        page([
          booking({ id: FIRST_ID, startsAt: "2026-06-30T17:00:00Z", endsAt: "2026-06-30T18:00:00Z" }),
          booking({ id: SECOND_ID, startsAt: "2026-06-30T18:00:00Z", endsAt: "2026-06-30T19:00:00Z" }),
          booking({ id: "0199f7a0-0000-7000-8000-0000000000b3", startsAt: "2026-07-01T17:00:00Z", endsAt: "2026-07-01T18:00:00Z" }),
          booking({ id: "0199f7a0-0000-7000-8000-0000000000b4", startsAt: "2026-07-02T17:00:00Z", endsAt: "2026-07-02T18:00:00Z" }),
        ]),
      );

      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(items()).toHaveLength(4));
      const headings = screen.getAllByRole("heading", { level: 3 }).map((heading) => heading.textContent);
      expect(headings).toEqual(["Hoje · terça-feira, 30 de junho", "Amanhã · quarta-feira, 1 de julho", "quinta-feira, 2 de julho"]);
      const today = screen.getByRole("list", { name: "Hoje · terça-feira, 30 de junho" });
      expect(within(today).getAllByRole("listitem")).toHaveLength(2);
    });

    it("says which booking of a weekly series each is («2 de 4»), and nothing for a single booking (US20)", async () => {
      const series = "0199f7a0-0000-7000-8000-0000000000e1";
      serveList(() =>
        page([
          booking({ id: FIRST_ID, series: { id: series, position: 1, count: 4 } }),
          booking({ id: SECOND_ID, series: { id: series, position: 2, count: 4 } }),
          booking({ id: "0199f7a0-0000-7000-8000-0000000000b3", series: null }),
        ]),
      );

      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(items()).toHaveLength(3));
      expect(items().map((item) => /Reserva semanal: (\d de \d)/.exec(row(item).details)?.[1])).toEqual(["1 de 4", "2 de 4", undefined]);
    });

    it("shows the times in Lisbon time whatever the browser's time zone, and marks the repeated October hour", async () => {
      // 00:30 UTC on 25 October 2026 is 01:30 WEST (first); 01:30 UTC is 01:30 WET (second).
      serveList(() =>
        page([
          booking({ id: FIRST_ID, startsAt: "2026-10-25T00:30:00Z", endsAt: "2026-10-25T01:00:00Z" }),
          booking({ id: SECOND_ID, startsAt: "2026-10-25T01:30:00Z", endsAt: "2026-10-25T02:00:00Z" }),
        ]),
      );

      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(items()).toHaveLength(2));
      expect(row(items()[0]).time).toBe("01:30 (verão) – 01:00 (inverno)");
      expect(row(items()[1]).time).toBe("01:30 (inverno) – 02:00");
    });

    it("switches to «Anteriores», shows the status in Portuguese and offers no cancelling there", async () => {
      const asked = serveList((query) =>
        query.get("scope") === "past"
          ? page([
              pastBooking({ id: "0199f7a0-0000-7000-8000-0000000000c1", status: "confirmed" }),
              pastBooking({ id: "0199f7a0-0000-7000-8000-0000000000c2", status: "cancelled_by_player", cancellationDeadline: null }),
              pastBooking({ id: "0199f7a0-0000-7000-8000-0000000000c3", status: "cancelled_by_club", cancellationDeadline: null }),
              pastBooking({ id: "0199f7a0-0000-7000-8000-0000000000c4", status: "no_show", cancellationDeadline: null }),
            ])
          : page([booking()]),
      );
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(1));

      await userEvent.click(tab("Anteriores"));

      await waitFor(() => expect(items()).toHaveLength(4));
      expect(currentPath()).toBe("/bookings?tab=past");
      expect(asked.at(-1)?.get("scope")).toBe("past");
      expect(tab("Anteriores")).toHaveAttribute("aria-current", "page");
      expect(panel()).toHaveAccessibleName("Reservas anteriores");
      // Screen readers get «Estado: …» (the prefix is visually hidden).
      const labels = items().map((item) => within(item).getByText(/^Estado:/).parentElement?.textContent);
      expect(labels).toEqual(["Estado: Confirmada", "Estado: Cancelada pelo jogador", "Estado: Cancelada pelo clube", "Estado: Falta"]);
      expect(screen.queryByRole("button", { name: "Cancelar reserva" })).not.toBeInTheDocument();
    });

    it("opens on the tab and page the address says", async () => {
      resetNavigation("/bookings?tab=past&page=2");
      const asked = serveList(() => page([pastBooking()], { page: 2, totalCount: 21 }));

      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(items()).toHaveLength(1));
      expect(asked[0].get("scope")).toBe("past");
      expect(asked[0].get("page")).toBe("2");
      expect(tab("Anteriores")).toHaveAttribute("aria-current", "page");
      expect(screen.getByText("Página 2 de 2")).toBeInTheDocument();
    });

    it.each(["abc", "0", "-3", "1.5", "999999"])("treats the page %s as the first page", async (value) => {
      resetNavigation(`/bookings?page=${value}`);
      const asked = serveList(() => page([booking()]));

      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(items()).toHaveLength(1));
      expect(asked[0].has("page")).toBe(false);
    });

    it("says when there is nothing, with a text for each tab", async () => {
      serveList(() => page([]));
      renderWithProviders(<MyBookings />);

      expect(await screen.findByText("Não tens reservas próximas. Pesquisa um clube para reservar um campo.")).toBeInTheDocument();

      await userEvent.click(tab("Anteriores"));

      expect(await screen.findByText("Ainda não tens reservas anteriores.")).toBeInTheDocument();
    });

    it("shows an error with a way to try again", async () => {
      let failing = true;
      serveList(() => (failing ? problem(500, "internal_error") : page([booking()])));
      renderWithProviders(<MyBookings />);

      expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      failing = false;
      await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

      await waitFor(() => expect(items()).toHaveLength(1));
    });

    it("sends a player whose session ended to the login page, to come back to the same tab", async () => {
      resetNavigation("/bookings?tab=past");
      serveList(() => problem(401, "unauthorized"));
      server.use(http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")));

      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(currentPath()).toBe("/login?next=%2Fbookings%3Ftab%3Dpast"));
    });
  });

  describe("pagination", () => {
    it("goes through the pages, 20 to a page, and the first page cannot go back", async () => {
      const asked = serveList((query) =>
        query.get("page") === "2" ? page([booking({ id: SECOND_ID })], { page: 2, totalCount: 41 }) : page([booking()], { page: 1, totalCount: 41 }),
      );
      renderWithProviders(<MyBookings />);
      await screen.findByText("Página 1 de 3");
      expect(screen.getByText("41 reservas")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Anterior" })).toBeDisabled();

      await userEvent.click(screen.getByRole("button", { name: "Seguinte" }));

      await screen.findByText("Página 2 de 3");
      expect(asked.at(-1)?.get("page")).toBe("2");
      expect(currentPath()).toBe("/bookings?page=2");
      expect(screen.getByRole("button", { name: "Anterior" })).toBeEnabled();

      await userEvent.click(screen.getByRole("button", { name: "Anterior" }));

      await screen.findByText("Página 1 de 3");
      expect(currentPath()).toBe("/bookings");
    });

    it("disables «Seguinte» on the last page", async () => {
      resetNavigation("/bookings?page=3");
      serveList(() => page([booking()], { page: 3, totalCount: 41 }));
      renderWithProviders(<MyBookings />);

      await screen.findByText("Página 3 de 3");

      expect(screen.getByRole("button", { name: "Seguinte" })).toBeDisabled();
    });

    it("shows no pager when everything fits on one page", async () => {
      serveList(() => page([booking()], { totalCount: 20 }));
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(1));

      expect(screen.queryByRole("navigation", { name: "Páginas" })).not.toBeInTheDocument();
    });

    it("goes back to the first page when changing tab", async () => {
      resetNavigation("/bookings?page=2");
      const asked = serveList(() => page([booking()], { page: 2, totalCount: 30 }));
      renderWithProviders(<MyBookings />);
      await screen.findByText("Página 2 de 2");

      await userEvent.click(tab("Anteriores"));

      await waitFor(() => expect(asked.at(-1)?.get("scope")).toBe("past"));
      expect(asked.at(-1)?.has("page")).toBe(false);
      expect(currentPath()).toBe("/bookings?tab=past");
    });

    it("offers the first page when the page asked for has nothing", async () => {
      resetNavigation("/bookings?page=9");
      serveList((query) => (query.get("page") === "9" ? page([], { page: 9, totalCount: 3 }) : page([booking()], { totalCount: 3 })));
      renderWithProviders(<MyBookings />);

      await userEvent.click(await screen.findByRole("button", { name: "Voltar à primeira página" }));

      await waitFor(() => expect(items()).toHaveLength(1));
      expect(currentPath()).toBe("/bookings");
    });
  });

  describe("cancelling", () => {
    it("asks to confirm first, and keeping the booking sends nothing", async () => {
      serveList(() => page([booking()]));
      let cancelled = 0;
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, () => {
          cancelled += 1;
          return HttpResponse.json(booking({ status: "cancelled_by_player", canCancel: false, cancellationDeadline: null }));
        }),
      );
      renderWithProviders(<MyBookings />);

      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      const confirmation = screen.getByRole("group", { name: "Confirmar cancelamento" });
      expect(within(confirmation).getByText(/Queres mesmo cancelar esta reserva\?/)).toBeInTheDocument();
      expect(cancelled).toBe(0);

      await userEvent.click(within(confirmation).getByRole("button", { name: "Manter reserva" }));

      expect(screen.queryByRole("group", { name: "Confirmar cancelamento" })).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Cancelar reserva" })).toBeEnabled();
      expect(cancelled).toBe(0);
    });

    it("cancels after the confirmation, says so, and the booking leaves «Próximas»", async () => {
      let isCancelled = false;
      const asked = serveList(() => page(isCancelled ? [] : [booking()]));
      const cancelled: string[] = [];
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, () => {
          cancelled.push(FIRST_ID);
          isCancelled = true;
          return HttpResponse.json(booking({ status: "cancelled_by_player", canCancel: false, cancellationDeadline: null }));
        }),
      );
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      // The result takes focus, which also brings it into view wherever the booking was in the list; it is not a live region,
      // or the screen reader would read it twice (UX-002, UX-003).
      const notice = await screen.findByText("Reserva cancelada. O horário voltou a ficar livre.");
      expect(notice).toHaveFocus();
      expect(notice).not.toHaveAttribute("role");
      expect(notice).not.toHaveAttribute("aria-live");
      expect(cancelled).toEqual([FIRST_ID]);
      await screen.findByText("Não tens reservas próximas. Pesquisa um clube para reservar um campo.");
      expect(asked).toHaveLength(2);
    });

    it("loads again my bookings and the free slots of the public pages once it is cancelled", async () => {
      serveList(() => page([booking()]));
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, () =>
          HttpResponse.json(booking({ status: "cancelled_by_player", canCancel: false, cancellationDeadline: null })),
        ),
      );
      const { queryClient } = renderWithProviders(<MyBookings />);
      const invalidate = vi.spyOn(queryClient, "invalidateQueries");
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      await screen.findByText("Reserva cancelada. O horário voltou a ficar livre.");
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["my-bookings"] });
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["catalog"] });
    });

    it("cannot be confirmed twice while the cancellation is under way", async () => {
      serveList(() => page([booking()]));
      let release: (() => void) | undefined;
      let requests = 0;
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, async () => {
          requests += 1;
          await new Promise<void>((resolve) => {
            release = resolve;
          });
          return HttpResponse.json(booking({ status: "cancelled_by_player", canCancel: false, cancellationDeadline: null }));
        }),
      );
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));
      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      const working = await screen.findByRole("button", { name: "A cancelar…" });

      expect(working).toBeDisabled();
      expect(screen.getByRole("button", { name: "Manter reserva" })).toBeDisabled();
      await userEvent.click(working);
      expect(requests).toBe(1);
      release?.();
      await screen.findByText("Reserva cancelada. O horário voltou a ficar livre.");
    });

    it("says until when it can be cancelled, in Lisbon time", async () => {
      serveList(() => page([booking()]));
      renderWithProviders(<MyBookings />);

      expect(await screen.findByText("Podes cancelar até 03/07/2026 18:00.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Cancelar reserva" })).toBeEnabled();
    });

    it("disables the button after the deadline and explains it with the deadline in Lisbon time", async () => {
      serveList(() => page([booking({ canCancel: false })]));
      renderWithProviders(<MyBookings />);

      const button = await screen.findByRole("button", { name: "Cancelar reserva" });

      expect(button).toBeDisabled();
      const explanation = "Já não é possível cancelar: o prazo terminou a 03/07/2026 18:00.";
      expect(screen.getByText(explanation)).toBeInTheDocument();
      expect(button).toHaveAccessibleDescription(explanation);
      await userEvent.click(button);
      expect(screen.queryByRole("group", { name: "Confirmar cancelamento" })).not.toBeInTheDocument();
    });

    it("shows the message and loads the list again when the club's deadline has passed meanwhile (422)", async () => {
      let late = false;
      const asked = serveList(() => page([booking({ canCancel: !late, cancellationDeadline: "2026-07-03T17:00:00Z" })]));
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, () => {
          late = true;
          return problem(422, "outside_cancellation_window");
        }),
      );
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      expect(await screen.findByText("O prazo para cancelar esta reserva já terminou.")).toHaveFocus();
      // The confirmation is closed and the list, loaded again, now shows the button disabled with the explanation.
      expect(screen.queryByRole("group", { name: "Confirmar cancelamento" })).not.toBeInTheDocument();
      await waitFor(() => expect(screen.getByRole("button", { name: "Cancelar reserva" })).toBeDisabled());
      expect(screen.getByText("Já não é possível cancelar: o prazo terminou a 03/07/2026 18:00.")).toBeInTheDocument();
      expect(asked).toHaveLength(2);
    });

    it("does not touch the public slots when the cancellation is refused", async () => {
      serveList(() => page([booking()]));
      server.use(http.post(`/api/bookings/${FIRST_ID}/cancel`, () => problem(422, "outside_cancellation_window")));
      const { queryClient } = renderWithProviders(<MyBookings />);
      const invalidate = vi.spyOn(queryClient, "invalidateQueries");
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      await screen.findByText("O prazo para cancelar esta reserva já terminou.");
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["my-bookings"] });
      expect(invalidate).not.toHaveBeenCalledWith({ queryKey: ["catalog"] });
    });

    it.each([
      ["booking_not_found", 404, "Esta reserva já não existe."],
      ["booking_not_confirmed", 422, "Esta reserva já não está confirmada, por isso não pode ser cancelada."],
    ])("shows the message for %s and loads the list again", async (code, status, message) => {
      const asked = serveList(() => page([booking()]));
      server.use(http.post(`/api/bookings/${FIRST_ID}/cancel`, () => problem(status, code)));
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      expect(await screen.findByText(message)).toHaveFocus();
      await waitFor(() => expect(asked).toHaveLength(2));
    });

    it("shows a toast, not a message above the list, when the server cannot be reached (UX-019)", async () => {
      serveList(() => page([booking()]));
      server.use(http.post(`/api/bookings/${FIRST_ID}/cancel`, () => HttpResponse.error()));
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      expect(await screen.findByText("Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.")).toBeInTheDocument();
      // Nothing is written in the list itself, and focus is back on the button that was pressed first.
      expect(panel().querySelector("[data-kind]")).toBeNull();
      expect(document.querySelector("[data-kind]")).toBeNull();
      await waitFor(() => expect(screen.getByRole("button", { name: "Cancelar reserva" })).toHaveFocus());
    });

    it("shows a toast for a 500, not a message above the list (UX-019)", async () => {
      serveList(() => page([booking()]));
      server.use(http.post(`/api/bookings/${FIRST_ID}/cancel`, () => problem(500, "internal_error")));
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
      expect(document.querySelector("[data-kind]")).toBeNull();
      expect(screen.getByRole("button", { name: "Cancelar reserva" })).toBeEnabled();
    });

    it("moves focus to the question when asking, and back to the button when the booking is kept (UX-002)", async () => {
      serveList(() => page([booking()]));
      renderWithProviders(<MyBookings />);
      const ask = await screen.findByRole("button", { name: "Cancelar reserva" });

      await userEvent.click(ask);
      expect(screen.getByRole("group", { name: "Confirmar cancelamento" })).toHaveFocus();

      await userEvent.click(screen.getByRole("button", { name: "Manter reserva" }));
      expect(screen.getByRole("button", { name: "Cancelar reserva" })).toHaveFocus();
    });

    it("sends the player to the login page when the session ended while deciding", async () => {
      resetNavigation("/bookings?tab=upcoming");
      serveList(() => page([booking()]));
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, () => problem(401, "unauthorized")),
        http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
      );
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      await waitFor(() => expect(currentPath()).toBe("/login?next=%2Fbookings"));
    });

    it("only asks to confirm one booking at a time", async () => {
      serveList(() => page([booking(), booking({ id: SECOND_ID, clubName: "Clube do Mar" })]));
      renderWithProviders(<MyBookings />);
      const [first, second] = await waitFor(() => {
        const found = items();
        expect(found).toHaveLength(2);
        return found;
      });

      await userEvent.click(within(first).getByRole("button", { name: "Cancelar reserva" }));
      await userEvent.click(within(second).getByRole("button", { name: "Cancelar reserva" }));

      expect(screen.getAllByRole("group", { name: "Confirmar cancelamento" })).toHaveLength(1);
      expect(within(items()[1]).getByRole("group", { name: "Confirmar cancelamento" })).toBeInTheDocument();
    });
  });


  describe("the choice between «Próximas» and «Anteriores» (UX-015)", () => {
    it("is two links in a navigation, not tabs: the current one says so, and each has an address of its own", async () => {
      serveList(() => page([booking()]));
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(1));

      expect(screen.queryByRole("tab")).not.toBeInTheDocument();
      expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
      expect(within(screen.getByRole("navigation", { name: "Próximas ou anteriores" })).getAllByRole("link")).toHaveLength(2);
      expect(tab("Próximas")).toHaveAttribute("href", "/bookings");
      expect(tab("Anteriores")).toHaveAttribute("href", "/bookings?tab=past");
    });

    it("works with the keyboard like any link: Tab to it, Enter to follow it", async () => {
      serveList((query) => (query.get("scope") === "past" ? page([pastBooking()]) : page([booking()])));
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(1));

      tab("Anteriores").focus();
      await userEvent.keyboard("{Enter}");

      await waitFor(() => expect(currentPath()).toBe("/bookings?tab=past"));
      expect(tab("Anteriores")).toHaveAttribute("aria-current", "page");
    });

    it("is at least 44px tall for a finger", async () => {
      serveList(() => page([booking()]));
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(1));

      expect(tab("Próximas").className).toContain("min-h-11");
      expect(tab("Anteriores").className).toContain("min-h-11");
    });
  });

  describe("where a result appears (UX-003)", () => {
    it("shows a refusal in the row of the booking it is about, with focus, and not above the list", async () => {
      serveList(() => page([booking({ id: FIRST_ID }), booking({ id: SECOND_ID, startsAt: "2026-07-04T19:00:00Z", endsAt: "2026-07-04T20:00:00Z" })]));
      server.use(http.post(`/api/bookings/${SECOND_ID}/cancel`, () => problem(422, "outside_cancellation_window")));
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(2));

      await userEvent.click(within(items()[1]).getByRole("button", { name: "Cancelar reserva" }));
      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      const refusal = await within(items()[1]).findByText("O prazo para cancelar esta reserva já terminou.");
      expect(refusal).toHaveFocus();
      expect(within(items()[0]).queryByText("O prazo para cancelar esta reserva já terminou.")).not.toBeInTheDocument();
    });

    it("shows that it worked above the list, in the same region, when the booking has left it", async () => {
      let done = false;
      serveList(() => page(done ? [] : [booking()]));
      server.use(
        http.post(`/api/bookings/${FIRST_ID}/cancel`, () => {
          done = true;
          return HttpResponse.json(booking({ status: "cancelled_by_player", canCancel: false, cancellationDeadline: null }));
        }),
      );
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      const notice = await screen.findByText("Reserva cancelada. O horário voltou a ficar livre.");
      expect(panel()).toContainElement(notice);
      expect(notice).toHaveFocus();
    });
  });

  describe("touch sizes (UX-022)", () => {
    it("makes the buttons of a booking and of the pager 44px tall on a phone", async () => {
      serveList(() => page([booking()], { totalCount: 41 }));
      renderWithProviders(<MyBookings />);
      await waitFor(() => expect(items()).toHaveLength(1));

      for (const name of ["Cancelar reserva", "Anterior", "Seguinte"]) {
        expect(screen.getByRole("button", { name }).className).toContain("h-11");
      }
    });
  });

  describe("structure and loading (UX-017, UX-020)", () => {
    it("goes from the h2 of the list to the h3 of each day, with no level skipped (UX-017)", async () => {
      serveList(() => page([booking()]));
      renderWithProviders(<MyBookings />);

      await waitFor(() => expect(items()).toHaveLength(1));

      const levels = screen.getAllByRole("heading").map((heading) => Number(heading.tagName.slice(1)));
      expect(levels).toEqual([2, 3]);
      expect(screen.getByRole("heading", { level: 2, name: "Reservas próximas" })).toBeInTheDocument();
    });

    it("shows the shape of the list while it loads, and says what is loading once", async () => {
      server.use(http.get("/api/me/bookings", async () => {
        await delay("infinite");
        return page([]);
      }));
      renderWithProviders(<MyBookings />);

      expect(await screen.findByRole("status")).toHaveTextContent("A carregar as reservas…");
      expect(document.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(1);
    });

    it("says there is nothing, with the shared empty state, when there are no bookings", async () => {
      serveList(() => page([]));
      renderWithProviders(<MyBookings />);

      expect(await screen.findByText("Não tens reservas próximas. Pesquisa um clube para reservar um campo.")).toBeInTheDocument();
    });
  });
});
