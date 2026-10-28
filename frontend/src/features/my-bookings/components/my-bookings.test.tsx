import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
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

const panel = () => screen.getByRole("tabpanel");
const items = () => within(screen.getByRole("list", { name: "Reservas" })).getAllByRole("listitem");

/** The label → value lines of one booking (the text between them may contain non-breaking spaces). */
function details(item: HTMLElement): Record<string, string> {
  const result: Record<string, string> = {};
  item.querySelectorAll("dt").forEach((term) => {
    result[term.textContent ?? ""] = (term.nextElementSibling?.textContent ?? "").replace(/\s/g, " ");
  });
  return result;
}

describe("MyBookings", () => {
  beforeEach(() => {
    resetNavigation("/bookings");
  });

  describe("the list", () => {
    it("opens on «Próximas» and shows club, pitch, date, time, price and status of each booking", async () => {
      const asked = serveList(() => page([booking()]));

      renderWithProviders(<MyBookings />);

      const [item] = await waitFor(() => {
        const found = items();
        expect(found).toHaveLength(1);
        return found;
      });
      expect(asked[0].get("scope")).toBe("upcoming");
      expect(asked[0].has("page")).toBe(false);
      expect(screen.getByRole("tab", { name: "Próximas" })).toHaveAttribute("aria-selected", "true");
      expect(screen.getByRole("tab", { name: "Anteriores" })).toHaveAttribute("aria-selected", "false");
      expect(within(item).getByRole("link", { name: "Clube da Foz" })).toHaveAttribute("href", "/clubs/clube-da-foz");
      expect(within(item).getByText("Padel 1 · Padel")).toBeInTheDocument();
      expect(details(item)).toEqual({
        Data: "sábado, 4 de julho (04/07/2026)",
        Hora: "18:00 – 19:00",
        Preço: "18,50 €",
      });
      expect(within(item).getByText("Confirmada")).toBeInTheDocument();
      expect(screen.getByText("1 reserva")).toBeInTheDocument();
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
      expect(details(items()[0]).Hora).toBe("01:30 (verão) – 01:00 (inverno)");
      expect(details(items()[1]).Hora).toBe("01:30 (inverno) – 02:00");
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

      await userEvent.click(screen.getByRole("tab", { name: "Anteriores" }));

      await waitFor(() => expect(items()).toHaveLength(4));
      expect(currentPath()).toBe("/bookings?tab=past");
      expect(asked.at(-1)?.get("scope")).toBe("past");
      expect(screen.getByRole("tab", { name: "Anteriores" })).toHaveAttribute("aria-selected", "true");
      expect(panel()).toHaveAccessibleName("Anteriores");
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
      expect(screen.getByRole("tab", { name: "Anteriores" })).toHaveAttribute("aria-selected", "true");
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

      await userEvent.click(screen.getByRole("tab", { name: "Anteriores" }));

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

      await userEvent.click(screen.getByRole("tab", { name: "Anteriores" }));

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

      expect(await screen.findByText("Reserva cancelada. O horário voltou a ficar livre.")).toHaveAttribute("role", "status");
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

      expect(await screen.findByRole("alert")).toHaveTextContent("O prazo para cancelar esta reserva já terminou.");
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

      await screen.findByRole("alert");
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

      expect(await screen.findByRole("alert")).toHaveTextContent(message);
      await waitFor(() => expect(asked).toHaveLength(2));
    });

    it("shows a toast, not a made-up message, for a failure that is not the API's", async () => {
      serveList(() => page([booking()]));
      server.use(http.post(`/api/bookings/${FIRST_ID}/cancel`, () => HttpResponse.error()));
      renderWithProviders(<MyBookings />);
      await userEvent.click(await screen.findByRole("button", { name: "Cancelar reserva" }));

      await userEvent.click(screen.getByRole("button", { name: "Sim, cancelar reserva" }));

      // A network failure is an ApiError with its own code, shown as a message above the list.
      expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível contactar o servidor.");
      expect(screen.getByRole("button", { name: "Cancelar reserva" })).toBeEnabled();
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
});
