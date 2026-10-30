import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRange } from "@/features/statistics/components/owner-statistics";
import { currentPath, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { AdminStats } from "../types";
import { AdminDashboard } from "./admin-dashboard";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const STATS: AdminStats = {
  owners: { active: 12, deactivated: 2 },
  clubs: { total: 20, visible: 14, ofDeactivatedOwners: 4, withoutActivePitches: 2 },
  activePitches: 61,
  players: { active: 120, deactivated: 1 },
  confirmedBookings: 15200,
};

const bucket = (start: string, owners: number, players: number, end = start) => ({ start, end, owners, players });

function registrations(patch: Record<string, unknown> = {}) {
  return {
    granularity: "week",
    from: "2026-09-28",
    to: "2026-10-18",
    owners: 3,
    players: 20,
    buckets: [bucket("2026-09-28", 1, 8, "2026-10-04"), bucket("2026-10-05", 0, 0, "2026-10-11"), bucket("2026-10-12", 2, 12, "2026-10-18")],
    ...patch,
  };
}

const club = (index: number, patch: Record<string, unknown> = {}) => ({
  clubId: `0199f7a0-0000-7000-8000-0000000000e${index}`,
  slug: `clube-${index}`,
  clubName: `Clube ${index}`,
  ownerName: `Dono ${index}`,
  locality: "Porto",
  occupation: 0.8125,
  bookedHours: 130,
  openHours: 160,
  ...patch,
});

const top = (clubs: Record<string, unknown>[] = [club(1), club(2, { occupation: 0.5, bookedHours: 62.5, openHours: 125, locality: "Maia" })]) => ({ from: "2026-09-26", to: "2026-10-25", clubs });

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) }, { status, headers: { "Content-Type": "application/problem+json" } });
}

/** Serves the whole page: the three things of US25, US27 and US28 and the owners; returns what each was asked. */
function serve(
  options: { registrations?: (query: URLSearchParams) => Response; top?: (query: URLSearchParams) => Response } = {},
) {
  const asked = { bookings: [] as URLSearchParams[], registrations: [] as URLSearchParams[], top: [] as URLSearchParams[] };
  server.use(
    http.get("/api/admin/bookings-over-time", ({ request }) => {
      asked.bookings.push(new URL(request.url).searchParams);
      return HttpResponse.json({ granularity: "day", from: "2026-09-29", to: "2026-10-01", includeCancelled: false, total: 0, buckets: [{ start: "2026-09-29", end: "2026-09-29", count: 0 }] });
    }),
    http.get("/api/admin/registrations", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.registrations.push(query);
      return options.registrations ? options.registrations(query) : HttpResponse.json(registrations());
    }),
    http.get("/api/admin/clubs/top-occupation", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.top.push(query);
      return options.top ? options.top(query) : HttpResponse.json(top());
    }),
    http.get("/api/admin/owners", () => HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 })),
  );
  return asked;
}

const text = (value: string | null) => (value ?? "").replace(/\s/g, " ");
const registrationsGroup = () => within(screen.getByRole("group", { name: "Agrupar registos por" }));
const registrationsTable = () => screen.getByRole("table", { name: "Registos em cada período" });
const topTable = () => screen.getByRole("table", { name: "Clubes com maior ocupação" });

describe("the admin's registrations and top of the clubs", () => {
  beforeEach(() => {
    resetNavigation("/admin");
  });

  describe("registrations (US28)", () => {
    it("asks for weeks over the last 30 days by default, and shows every period, an empty one as 0", async () => {
      const asked = serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await screen.findByRole("table", { name: "Registos em cada período" });
      expect(Object.fromEntries(asked.registrations[0])).toEqual({ granularity: "week" });
      const rowsOf = within(registrationsTable()).getAllByRole("row").slice(1);
      expect(rowsOf.map((row) => [within(row).getByRole("rowheader").textContent, ...within(row).getAllByRole("cell").map((cell) => cell.textContent)])).toEqual([
        ["28/09/2026 a 04/10/2026", "1", "8"],
        ["05/10/2026 a 11/10/2026", "0", "0"],
        ["12/10/2026 a 18/10/2026", "2", "12"],
      ]);
      expect(screen.getByText(/3 donos e 20 jogadores registados entre 28\/09\/2026 e 18\/10\/2026\./)).toBeInTheDocument();
      expect(registrationsGroup().getByRole("button", { name: "Semana" })).toHaveAttribute("aria-pressed", "true");
      expect(screen.getByRole("img", { name: /donos e os jogadores registados/ })).toBeInTheDocument();
    });

    it("offers the numbers as a table anyone can open, not only to screen readers (UX-027)", async () => {
      serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const table = await screen.findByRole("table", { name: "Registos em cada período" });
      const details = table.closest("details");
      expect(details).not.toBeNull();
      expect(within(details as HTMLElement).getByText("Ver os números numa tabela")).toBeInTheDocument();
      expect(table.closest(".sr-only")).toBeNull();
    });

    it("switches to months, asks again and writes the choice in the URL, leaving the bookings chart alone", async () => {
      const asked = serve({
        registrations: (query) =>
          HttpResponse.json(query.get("granularity") === "month" ? registrations({ granularity: "month", from: "2026-09-01", to: "2026-10-31", buckets: [bucket("2026-09-01", 1, 8, "2026-09-30"), bucket("2026-10-01", 2, 12, "2026-10-31")] }) : registrations()),
      });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Registos em cada período" });
      const bookingsAsked = asked.bookings.length;

      await user.click(registrationsGroup().getByRole("button", { name: "Mês" }));

      await waitFor(() => expect(asked.registrations.at(-1)?.get("granularity")).toBe("month"));
      expect(currentPath()).toBe("/admin?registrations=month");
      await waitFor(() => expect(within(registrationsTable()).getAllByRole("row")[1]).toHaveTextContent("setembro de 2026"));
      expect(registrationsGroup().getByRole("button", { name: "Mês" })).toHaveAttribute("aria-pressed", "true");
      expect(asked.bookings.length).toBe(bookingsAsked);
    });

    it("reads the grouping from the URL, and ignores nonsense", async () => {
      resetNavigation("/admin?registrations=month");
      const asked = serve();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await waitFor(() => expect(asked.registrations).toHaveLength(1));
      expect(asked.registrations[0].get("granularity")).toBe("month");
    });

    it("never offers days", async () => {
      serve();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Registos em cada período" });

      expect(registrationsGroup().queryByRole("button", { name: "Dia" })).not.toBeInTheDocument();
    });

    it("says nobody registered, and still draws every period, when there are none", async () => {
      serve({ registrations: () => HttpResponse.json(registrations({ owners: 0, players: 0, buckets: [bucket("2026-09-28", 0, 0, "2026-10-04")] })) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("Ainda não houve registos neste intervalo.")).toBeInTheDocument();
      expect(within(within(registrationsTable()).getAllByRole("row")[1]).getAllByRole("cell").map((cell) => cell.textContent)).toEqual(["0", "0"]);
    });

    it("says one owner and one player in the singular", async () => {
      serve({ registrations: () => HttpResponse.json(registrations({ owners: 1, players: 1 })) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText(/1 dono e 1 jogador registados/)).toBeInTheDocument();
    });

    it("shows the API's message for a refused range, and the rest of the page keeps working", async () => {
      serve({ registrations: () => problem(400, "validation_failed", { to: ["O intervalo tem demasiados períodos: no máximo 104 semanas."] }) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("O intervalo tem demasiados períodos: no máximo 104 semanas.")).toBeInTheDocument();
      expect(await screen.findByRole("table", { name: "Clubes com maior ocupação" })).toBeInTheDocument();
    });

    it("shows a message, and tries again on request, for any other failure", async () => {
      let fail = true;
      serve({ registrations: () => (fail ? problem(500, "internal_error") : HttpResponse.json(registrations())) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const message = await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      fail = false;
      await user.click(within(message.closest("div")!).getByRole("button", { name: "Tentar novamente" }));

      expect(await screen.findByRole("table", { name: "Registos em cada período" })).toBeInTheDocument();
    });
  });

  describe("the clubs with the highest occupation (US27)", () => {
    it("lists the clubs in the order the API gives, with owner, locality, occupation in % and hours in pt-PT", async () => {
      const asked = serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await screen.findByRole("table", { name: "Clubes com maior ocupação" });
      expect(Object.fromEntries(asked.top[0])).toEqual({});
      const rowsOf = within(topTable()).getAllByRole("row").slice(1);
      expect(rowsOf.map((row) => text(row.textContent))).toEqual([
        "11. Clube 1Dono 1Porto81,3 %130 h160 h",
        "22. Clube 2Dono 2Maia50 %62,5 h125 h",
      ]);
      expect(within(rowsOf[0]).getByRole("link", { name: "Clube 1" })).toHaveAttribute("href", "/clubs/clube-1");
      expect(screen.getByText("De 26/09/2026 a 25/10/2026.")).toBeInTheDocument();
    });

    it("has nothing about players on it", async () => {
      serve();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Clubes com maior ocupação" });

      expect(topTable().textContent).not.toMatch(/jogador/i);
      expect(within(topTable()).getAllByRole("columnheader").map((header) => header.textContent)).toEqual([
        "#", "Clube", "Dono", "Localidade", "Ocupação", "Horas reservadas", "Horas abertas",
      ]);
    });

    it("says no club had open hours in the period, when the list is empty", async () => {
      serve({ top: () => HttpResponse.json(top([])) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("Nenhum clube teve horas abertas entre 26/09/2026 e 25/10/2026.")).toBeInTheDocument();
      expect(screen.queryByRole("table", { name: "Clubes com maior ocupação" })).not.toBeInTheDocument();
    });

    it("shows the API's message for a refused period", async () => {
      serve({ top: () => problem(400, "validation_failed", { to: ["A data final não pode ser anterior à inicial."] }) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("A data final não pode ser anterior à inicial.")).toBeInTheDocument();
    });

    it("shows a message, and tries again on request, for any other failure", async () => {
      let fail = true;
      serve({ top: () => (fail ? problem(500, "internal_error") : HttpResponse.json(top())) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const message = await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      fail = false;
      await user.click(within(message.closest("div")!).getByRole("button", { name: "Tentar novamente" }));

      expect(await screen.findByRole("table", { name: "Clubes com maior ocupação" })).toBeInTheDocument();
    });
  });

  describe("the period the three share", () => {
    it("shows the last 30 days in its fields until one is chosen", async () => {
      serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const last30 = defaultRange();
      expect(await screen.findByLabelText("De")).toHaveValue(last30.from);
      expect(screen.getByLabelText("Até")).toHaveValue(last30.to);
      expect(screen.getByText("Vale para as reservas, os registos e os clubes com maior ocupação.")).toBeInTheDocument();
    });

    it("sends the same dates to the bookings, the registrations and the top when they change", async () => {
      const asked = serve();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Clubes com maior ocupação" });

      fireEvent.change(screen.getByLabelText("De"), { target: { value: "2026-07-01" } });
      await waitFor(() => expect(asked.top.at(-1)?.get("from")).toBe("2026-07-01"));
      fireEvent.change(screen.getByLabelText("Até"), { target: { value: "2026-09-30" } });
      await waitFor(() => expect(asked.top.at(-1)?.get("to")).toBe("2026-09-30"));

      for (const queries of [asked.bookings, asked.registrations, asked.top]) {
        await waitFor(() => expect(queries.at(-1)?.get("from")).toBe("2026-07-01"));
        expect(queries.at(-1)?.get("to")).toBe("2026-09-30");
      }

      expect(currentPath()).toBe("/admin?from=2026-07-01&to=2026-09-30");
    });

    it("reads it from the URL for all three, together with the grouping, and the way back is the last 30 days", async () => {
      resetNavigation("/admin?from=2026-07-01&to=2026-09-30&registrations=month&granularity=week");
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await waitFor(() => expect(asked.top).toHaveLength(1));
      expect(Object.fromEntries(asked.top[0])).toEqual({ from: "2026-07-01", to: "2026-09-30" });
      expect(Object.fromEntries(asked.registrations[0])).toEqual({ granularity: "month", from: "2026-07-01", to: "2026-09-30" });
      expect(Object.fromEntries(asked.bookings[0])).toEqual({ granularity: "week", from: "2026-07-01", to: "2026-09-30" });
      expect(screen.getByLabelText("De")).toHaveValue("2026-07-01");

      await user.click(screen.getByRole("button", { name: "Últimos 30 dias" }));

      await waitFor(() => expect(asked.top.at(-1)?.has("from")).toBe(false));
      expect(currentPath()).toBe("/admin?granularity=week&registrations=month");
    });

    it("the bookings chart no longer has fields of its own", async () => {
      serve();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Clubes com maior ocupação" });

      expect(screen.getAllByLabelText("De")).toHaveLength(1);
      expect(screen.getAllByLabelText("Até")).toHaveLength(1);
    });
  });
});
