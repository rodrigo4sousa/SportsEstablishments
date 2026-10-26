import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { AdminStats } from "../types";
import { defaultRange } from "@/features/statistics/components/owner-statistics";
import { AdminDashboard } from "./admin-dashboard";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const STATS: AdminStats = {
  owners: { active: 12, deactivated: 2 },
  clubs: { total: 20, visible: 14, ofDeactivatedOwners: 4, withoutActivePitches: 2 },
  activePitches: 61,
  players: { active: 1234, deactivated: 1 },
  confirmedBookings: 15200,
};

const bucket = (start: string, count: number, end = start) => ({ start, end, count });

function chartResponse(patch: Record<string, unknown> = {}) {
  return {
    granularity: "day",
    from: "2026-09-29",
    to: "2026-10-01",
    includeCancelled: false,
    total: 5,
    buckets: [bucket("2026-09-29", 2), bucket("2026-09-30", 0), bucket("2026-10-01", 3)],
    ...patch,
  };
}

function registrationsResponse(patch: Record<string, unknown> = {}) {
  return {
    granularity: "week",
    from: "2026-09-28",
    to: "2026-10-11",
    owners: 3,
    players: 20,
    buckets: [
      { start: "2026-09-28", end: "2026-10-04", owners: 1, players: 8 },
      { start: "2026-10-05", end: "2026-10-11", owners: 2, players: 12 },
    ],
    ...patch,
  };
}

function topResponse(patch: Record<string, unknown> = {}) {
  return {
    from: "2026-09-26",
    to: "2026-10-25",
    clubs: [
      { clubId: "0199f7a0-0000-7000-8000-0000000000e1", slug: "clube-da-foz", clubName: "Clube da Foz", ownerName: "Ana Dona", locality: "Porto", occupation: 0.8125, bookedHours: 130, openHours: 160 },
      { clubId: "0199f7a0-0000-7000-8000-0000000000e2", slug: "padel-maia", clubName: "Padel Maia", ownerName: "Rui Dono", locality: "Maia", occupation: 0.5, bookedHours: 62.5, openHours: 125 },
    ],
    ...patch,
  };
}

function ownerRow(patch: Record<string, unknown> = {}) {
  return {
    id: "0199f7a0-0000-7000-8000-000000000001",
    name: "Ana Dona",
    email: "ana@clube.test",
    registeredAt: "2026-07-14T09:30:00Z",
    deactivated: false,
    clubs: 2,
    pitches: 7,
    bookings: 1234,
    players: 80,
    ...patch,
  };
}

function ownersResponse(items = [ownerRow()], extra: { page?: number; totalCount?: number } = {}) {
  return { items, page: extra.page ?? 1, pageSize: 20, totalCount: extra.totalCount ?? items.length };
}

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) }, { status, headers: { "Content-Type": "application/problem+json" } });
}

/** Serves the three endpoints; the returned arrays hold the query string of every request each one received. */
function serve(
  options: {
    chart?: (query: URLSearchParams) => Response;
    owners?: (query: URLSearchParams) => Response;
    stats?: () => Response;
    registrations?: (query: URLSearchParams) => Response;
    top?: (query: URLSearchParams) => Response;
  } = {},
) {
  const asked = { chart: [] as URLSearchParams[], owners: [] as URLSearchParams[], registrations: [] as URLSearchParams[], top: [] as URLSearchParams[], stats: 0 };
  server.use(
    http.get("/api/admin/stats", () => {
      asked.stats += 1;
      return options.stats ? options.stats() : HttpResponse.json(STATS);
    }),
    http.get("/api/admin/bookings-over-time", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.chart.push(query);
      return options.chart ? options.chart(query) : HttpResponse.json(chartResponse());
    }),
    http.get("/api/admin/registrations", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.registrations.push(query);
      return options.registrations ? options.registrations(query) : HttpResponse.json(registrationsResponse());
    }),
    http.get("/api/admin/clubs/top-occupation", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.top.push(query);
      return options.top ? options.top(query) : HttpResponse.json(topResponse());
    }),
    http.get("/api/admin/owners", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.owners.push(query);
      return options.owners ? options.owners(query) : HttpResponse.json(ownersResponse());
    }),
  );
  return asked;
}

const totals = () => screen.getByRole("list", { name: "Totais da plataforma" });
const chartGroup = () => within(screen.getByRole("group", { name: "Agrupar por" }));
const chartTable = () => screen.getByRole("table", { name: "Reservas feitas em cada período" });
const ownersTable = () => screen.getByRole("table", { name: "Donos e os seus números" });

describe("AdminDashboard", () => {
  beforeEach(() => {
    resetNavigation("/admin");
  });

  describe("totals (US24)", () => {
    it("shows the totals the server read, with deactivated accounts and clubs apart, and asks for nothing", async () => {
      const asked = serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const cards = within(totals()).getAllByRole("listitem");
      expect(cards).toHaveLength(5);
      const [owners, clubs, pitches, players, bookings] = cards;
      expect(within(owners).getByRole("heading", { name: "Donos" })).toBeInTheDocument();
      expect(owners).toHaveTextContent("12");
      expect(owners).toHaveTextContent("2 donos desativados");
      expect(clubs).toHaveTextContent("Clubes visíveis");
      expect(clubs).toHaveTextContent("14");
      expect(clubs).toHaveTextContent("4 clubes de donos desativados");
      expect(clubs).toHaveTextContent("2 clubes sem campos ativos");
      expect(pitches).toHaveTextContent("61");
      expect(players).toHaveTextContent("1234");  // pt-PT does not group four digits
      expect(players).toHaveTextContent("1 jogador desativado");
      expect(bookings.textContent?.replace(/\s/g, " ")).toContain("15 200");
      await waitFor(() => expect(asked.chart).toHaveLength(1));
      expect(asked.stats).toBe(0);
    });

    it("loads the totals in the browser when the server could not", async () => {
      serve();

      renderWithProviders(<AdminDashboard initialStats={null} />);

      expect(await screen.findByText("A carregar os totais…")).toBeInTheDocument();
      expect(await within(await screen.findByRole("list", { name: "Totais da plataforma" })).findByText("61")).toBeInTheDocument();
    });

    it("says so and lets the admin try again when the totals fail", async () => {
      let fail = true;
      serve({ stats: () => (fail ? problem(500, "internal_error") : HttpResponse.json(STATS)) });
      const user = userEvent.setup();

      renderWithProviders(<AdminDashboard initialStats={null} />);

      const alert = await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      fail = false;
      await user.click(within(alert.closest("div")!).getByRole("button", { name: "Tentar novamente" }));
      expect(await screen.findByRole("list", { name: "Totais da plataforma" })).toBeInTheDocument();
    });
  });

  describe("chart (US25)", () => {
    it("asks for the last 30 days by day, without cancelled ones, by default", async () => {
      const asked = serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await waitFor(() => expect(asked.chart).toHaveLength(1));
      expect(Object.fromEntries(asked.chart[0])).toEqual({ granularity: "day" });
      expect(chartGroup().getByRole("button", { name: "Dia" })).toHaveAttribute("aria-pressed", "true");
      expect(chartGroup().getByRole("button", { name: "Semana" })).toHaveAttribute("aria-pressed", "false");
      expect(screen.getByLabelText("Incluir canceladas")).not.toBeChecked();
    });

    it("shows the range the API answered in the summary, and the last 30 days in the shared fields", async () => {
      serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText(/5 reservas feitas entre 29\/09\/2026 e 01\/10\/2026\./)).toBeInTheDocument();
      const last30 = defaultRange();
      expect(screen.getByLabelText("De")).toHaveValue(last30.from);
      expect(screen.getByLabelText("Até")).toHaveValue(last30.to);
    });

    it("lists every period, a period without bookings with a 0", async () => {
      serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await screen.findByRole("table", { name: "Reservas feitas em cada período" });
      const rows = within(chartTable()).getAllByRole("row").slice(1);
      expect(rows.map((row) => row.textContent)).toEqual(["29/09/20262", "30/09/20260", "01/10/20263"]);
      expect(screen.getByRole("img", { name: /reservas feitas em cada período/ })).toBeInTheDocument();
    });

    it("names a week by its first and last day and a month by its name", async () => {
      serve({
        chart: (query) =>
          HttpResponse.json(
            query.get("granularity") === "month"
              ? chartResponse({ granularity: "month", from: "2026-08-01", to: "2026-10-31", buckets: [bucket("2026-08-01", 0, "2026-08-31"), bucket("2026-10-01", 4, "2026-10-31")] })
              : chartResponse({ granularity: "week", from: "2026-10-19", to: "2026-11-01", buckets: [bucket("2026-10-19", 7, "2026-10-25"), bucket("2026-10-26", 0, "2026-11-01")] }),
          ),
      });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await user.click(chartGroup().getByRole("button", { name: "Semana" }));
      await waitFor(() => expect(within(chartTable()).getAllByRole("row")[1].textContent).toBe("19/10/2026 a 25/10/20267"));
      await user.click(chartGroup().getByRole("button", { name: "Mês" }));
      await waitFor(() => expect(within(chartTable()).getAllByRole("row")[2].textContent).toBe("outubro de 20264"));
    });

    it("asks again, and writes the URL, when the granularity changes", async () => {
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await waitFor(() => expect(asked.chart).toHaveLength(1));

      await user.click(chartGroup().getByRole("button", { name: "Mês" }));

      await waitFor(() => expect(asked.chart.at(-1)?.get("granularity")).toBe("month"));
      expect(currentPath()).toBe("/admin?granularity=month");
      expect(chartGroup().getByRole("button", { name: "Mês" })).toHaveAttribute("aria-pressed", "true");
    });

    it("sends the chosen dates and keeps them when the granularity changes", async () => {
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByText(/5 reservas feitas/);

      fireEvent.change(screen.getByLabelText("De"), { target: { value: "2026-08-01" } });
      await waitFor(() => expect(asked.chart.at(-1)?.get("from")).toBe("2026-08-01"));
      fireEvent.change(screen.getByLabelText("Até"), { target: { value: "2026-09-30" } });
      await waitFor(() => expect(asked.chart.at(-1)?.get("to")).toBe("2026-09-30"));
      await user.click(chartGroup().getByRole("button", { name: "Semana" }));

      await waitFor(() => expect(Object.fromEntries(asked.chart.at(-1)!)).toEqual({ granularity: "week", from: "2026-08-01", to: "2026-09-30" }));
      expect(currentPath()).toBe("/admin?granularity=week&from=2026-08-01&to=2026-09-30");
    });

    it("goes back to the last 30 days", async () => {
      resetNavigation("/admin?from=2026-08-01&to=2026-09-30");
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await waitFor(() => expect(asked.chart).toHaveLength(1));
      expect(Object.fromEntries(asked.chart[0])).toEqual({ granularity: "day", from: "2026-08-01", to: "2026-09-30" });

      await user.click(screen.getByRole("button", { name: "Últimos 30 dias" }));

      await waitFor(() => expect(Object.fromEntries(asked.chart.at(-1)!)).toEqual({ granularity: "day" }));
      expect(currentPath()).toBe("/admin");
      expect(screen.getByRole("button", { name: "Últimos 30 dias" })).toBeDisabled();
    });

    it("includes cancelled bookings when asked, and says so", async () => {
      const asked = serve({
        chart: (query) =>
          HttpResponse.json(
            query.get("includeCancelled") === "true"
              ? chartResponse({ includeCancelled: true, total: 9, buckets: [bucket("2026-09-29", 4), bucket("2026-09-30", 1), bucket("2026-10-01", 4)] })
              : chartResponse(),
          ),
      });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByText(/5 reservas feitas/);

      await user.click(screen.getByLabelText("Incluir canceladas"));

      expect(await screen.findByText(/9 reservas feitas entre 29\/09\/2026 e 01\/10\/2026, com as canceladas\./)).toBeInTheDocument();
      expect(asked.chart.at(-1)?.get("includeCancelled")).toBe("true");
      expect(currentPath()).toBe("/admin?cancelled=1");
      expect(screen.getByLabelText("Incluir canceladas")).toBeChecked();
      expect(within(chartTable()).getAllByRole("row")[1].textContent).toBe("29/09/20264");
    });

    it("reads the whole view from the URL", async () => {
      resetNavigation("/admin?granularity=week&from=2026-08-01&to=2026-09-30&cancelled=1");
      const asked = serve({ chart: () => HttpResponse.json(chartResponse({ granularity: "week", includeCancelled: true })) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await waitFor(() => expect(asked.chart).toHaveLength(1));
      expect(Object.fromEntries(asked.chart[0])).toEqual({ granularity: "week", from: "2026-08-01", to: "2026-09-30", includeCancelled: "true" });
      expect(chartGroup().getByRole("button", { name: "Semana" })).toHaveAttribute("aria-pressed", "true");
      expect(screen.getByLabelText("Incluir canceladas")).toBeChecked();
      expect(screen.getByLabelText("De")).toHaveValue("2026-08-01");
    });

    it("ignores nonsense in the URL", async () => {
      resetNavigation("/admin?granularity=year&from=ontem&to=2026-02-30&cancelled=yes");
      const asked = serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await waitFor(() => expect(asked.chart).toHaveLength(1));
      expect(Object.fromEntries(asked.chart[0])).toEqual({ granularity: "day" });
    });

    it("says there were no bookings, and still draws every period at 0", async () => {
      serve({ chart: () => HttpResponse.json(chartResponse({ total: 0, buckets: [bucket("2026-09-29", 0), bucket("2026-09-30", 0), bucket("2026-10-01", 0)] })) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("Ainda não houve reservas neste intervalo.")).toBeInTheDocument();
      expect(screen.getByText(/0 reservas feitas entre/)).toBeInTheDocument();
      expect(within(chartTable()).getAllByRole("row").slice(1).map((row) => row.lastChild?.textContent)).toEqual(["0", "0", "0"]);
    });

    it("shows the API's message for a range that is refused, and the others keep working", async () => {
      serve({ chart: () => problem(400, "validation_failed", { to: ["O intervalo tem demasiados períodos: no máximo 366 dias."] }) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("O intervalo tem demasiados períodos: no máximo 366 dias.")).toBeInTheDocument();
      expect(screen.getByRole("table", { name: "Donos e os seus números" })).toBeInTheDocument();
    });

    it("shows a message for any other failure of the chart", async () => {
      serve({ chart: () => problem(500, "internal_error") });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
    });
  });

  describe("owners (US26)", () => {
    it("asks for the first page by name, and shows each owner with the date in Lisbon and deactivated ones marked", async () => {
      const asked = serve({
        owners: () =>
          HttpResponse.json(
            ownersResponse([ownerRow(), ownerRow({ id: "0199f7a0-0000-7000-8000-000000000002", name: "Rui Dono", email: "rui@clube.test", deactivated: true, clubs: 0, pitches: 0, bookings: 0, players: 0 })]),
          ),
      });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await screen.findByRole("table", { name: "Donos e os seus números" });
      expect(Object.fromEntries(asked.owners[0])).toEqual({ sort: "name", direction: "asc" });
      const [ana, rui] = within(ownersTable()).getAllByRole("row").slice(1);
      expect(within(ana).getByRole("rowheader")).toHaveTextContent("Ana Dona");
      // 09:30 UTC on 14 July is 10:30 in Lisbon, the same date; pt-PT does not group four digits.
      expect(within(ana).getAllByRole("cell").map((cell) => cell.textContent)).toEqual(["ana@clube.test", "14/07/2026", "Ativo", "2", "7", "1234", "80"]);
      expect(within(rui).getByText("Desativado")).toBeInTheDocument();
      expect(within(ana).queryByText("Desativado")).not.toBeInTheDocument();
      expect(screen.getByText("2 donos")).toBeInTheDocument();
    });

    it("links each owner's name to the owner's page", async () => {
      serve();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const link = await within(await screen.findByRole("table", { name: "Donos e os seus números" })).findByRole("link", { name: "Ana Dona" });

      expect(link).toHaveAttribute("href", "/admin/owners/0199f7a0-0000-7000-8000-000000000001");
    });

    it("sorts by a header, reverses on the second click, and marks the column", async () => {
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Donos e os seus números" });
      expect(within(ownersTable()).getByRole("columnheader", { name: /Nome/ })).toHaveAttribute("aria-sort", "ascending");

      await user.click(screen.getByRole("button", { name: "Reservas" }));
      await waitFor(() => expect(Object.fromEntries(asked.owners.at(-1)!)).toEqual({ sort: "bookings", direction: "asc" }));
      expect(within(ownersTable()).getByRole("columnheader", { name: /Reservas/ })).toHaveAttribute("aria-sort", "ascending");
      expect(within(ownersTable()).getByRole("columnheader", { name: /Nome/ })).toHaveAttribute("aria-sort", "none");

      await user.click(screen.getByRole("button", { name: /Reservas/ }));
      await waitFor(() => expect(Object.fromEntries(asked.owners.at(-1)!)).toEqual({ sort: "bookings", direction: "desc" }));
      expect(within(ownersTable()).getByRole("columnheader", { name: /Reservas/ })).toHaveAttribute("aria-sort", "descending");
      expect(currentPath()).toBe("/admin?sort=bookings&direction=desc");
    });

    it.each([
      ["Nome", "name"],
      ["Email", "email"],
      ["Registo", "registeredAt"],
      ["Estado", "status"],
      ["Clubes", "clubs"],
      ["Campos", "pitches"],
      ["Reservas", "bookings"],
      ["Jogadores", "players"],
    ])("the %s header sorts by %s", async (label, sort) => {
      // Start somewhere else, so that every header, «Nome» included, is a change.
      resetNavigation(sort === "name" ? "/admin?sort=email" : "/admin");
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Donos e os seus números" });

      await user.click(within(ownersTable()).getByRole("button", { name: new RegExp(`^${label}`) }));

      await waitFor(() => expect(asked.owners.at(-1)?.get("sort")).toBe(sort));
    });

    it("searches by name or email when the form is sent, from the first page", async () => {
      resetNavigation("/admin?page=3");
      const asked = serve({ owners: () => HttpResponse.json(ownersResponse([ownerRow()], { page: 3, totalCount: 45 })) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Donos e os seus números" });

      await user.type(screen.getByLabelText("Pesquisar dono"), "  ANA ");
      expect(asked.owners).toHaveLength(1);
      await user.click(screen.getByRole("button", { name: "Pesquisar" }));

      await waitFor(() => expect(Object.fromEntries(asked.owners.at(-1)!)).toEqual({ sort: "name", direction: "asc", search: "ANA" }));
      expect(currentPath()).toBe("/admin?search=ANA");
    });

    it("clears the search", async () => {
      resetNavigation("/admin?search=ana");
      const asked = serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Donos e os seus números" });
      expect(screen.getByLabelText("Pesquisar dono")).toHaveValue("ana");
      expect(asked.owners[0].get("search")).toBe("ana");

      await user.click(screen.getByRole("button", { name: "Limpar" }));

      await waitFor(() => expect(asked.owners.at(-1)?.has("search")).toBe(false));
      expect(screen.getByLabelText("Pesquisar dono")).toHaveValue("");
      expect(currentPath()).toBe("/admin");
    });

    it("pages through the owners, 20 to a page", async () => {
      const asked = serve({ owners: (query) => HttpResponse.json(ownersResponse([ownerRow()], { page: Number(query.get("page") ?? 1), totalCount: 45 })) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByText("Página 1 de 3");
      const pages = screen.getByRole("navigation", { name: "Páginas de donos" });
      expect(within(pages).getByRole("button", { name: "Anterior" })).toBeDisabled();

      await user.click(within(pages).getByRole("button", { name: "Seguinte" }));

      expect(await screen.findByText("Página 2 de 3")).toBeInTheDocument();
      expect(asked.owners.at(-1)?.get("page")).toBe("2");
      expect(currentPath()).toBe("/admin?page=2");
      await user.click(within(pages).getByRole("button", { name: "Seguinte" }));
      expect(await screen.findByText("Página 3 de 3")).toBeInTheDocument();
      expect(within(pages).getByRole("button", { name: "Seguinte" })).toBeDisabled();
      await user.click(within(pages).getByRole("button", { name: "Anterior" }));
      expect(await screen.findByText("Página 2 de 3")).toBeInTheDocument();
    });

    it("goes back to page 1 when the order changes", async () => {
      resetNavigation("/admin?page=2");
      const asked = serve({ owners: (query) => HttpResponse.json(ownersResponse([ownerRow()], { page: Number(query.get("page") ?? 1), totalCount: 45 })) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByText("Página 2 de 3");

      await user.click(screen.getByRole("button", { name: "Email" }));

      await screen.findByText("Página 1 de 3");
      expect(asked.owners.at(-1)?.has("page")).toBe(false);
    });

    it("reads search, order and page from the URL", async () => {
      resetNavigation("/admin?search=ana&sort=players&direction=desc&page=2");
      const asked = serve({ owners: () => HttpResponse.json(ownersResponse([ownerRow()], { page: 2, totalCount: 45 })) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await screen.findByText("Página 2 de 3");
      expect(Object.fromEntries(asked.owners[0])).toEqual({ sort: "players", direction: "desc", search: "ana", page: "2" });
      expect(within(ownersTable()).getByRole("columnheader", { name: /Jogadores/ })).toHaveAttribute("aria-sort", "descending");
      expect(screen.getByLabelText("Pesquisar dono")).toHaveValue("ana");
    });

    it("ignores nonsense in the URL", async () => {
      resetNavigation("/admin?sort=password&direction=sideways&page=0");
      const asked = serve();

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await waitFor(() => expect(asked.owners).toHaveLength(1));
      expect(Object.fromEntries(asked.owners[0])).toEqual({ sort: "name", direction: "asc" });
    });

    it("keeps the chart's filters when the table changes, and the other way round", async () => {
      resetNavigation("/admin?granularity=week&search=ana");
      serve();
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      await screen.findByRole("table", { name: "Donos e os seus números" });

      await user.click(screen.getByRole("button", { name: "Clubes" }));
      await waitFor(() => expect(currentPath()).toBe("/admin?granularity=week&search=ana&sort=clubs"));
      await user.click(screen.getByLabelText("Incluir canceladas"));
      await waitFor(() => expect(currentPath()).toBe("/admin?granularity=week&cancelled=1&search=ana&sort=clubs"));
    });

    it("says there are no owners, and that nothing matches a search", async () => {
      serve({ owners: () => HttpResponse.json(ownersResponse([])) });
      const { unmount } = renderWithProviders(<AdminDashboard initialStats={STATS} />);
      expect(await screen.findByText("Ainda não há donos registados.")).toBeInTheDocument();
      unmount();

      resetNavigation("/admin?search=zzz");
      renderWithProviders(<AdminDashboard initialStats={STATS} />);
      expect(await screen.findByText("Nenhum dono corresponde à pesquisa.")).toBeInTheDocument();
    });

    it("offers the first page when the page asked for is empty", async () => {
      resetNavigation("/admin?page=9");
      const asked = serve({ owners: (query) => HttpResponse.json(query.has("page") ? ownersResponse([], { page: 9, totalCount: 45 }) : ownersResponse([ownerRow()], { totalCount: 45 })) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      await user.click(await screen.findByRole("button", { name: "Voltar à primeira página" }));

      expect(await screen.findByText("Página 1 de 3")).toBeInTheDocument();
      expect(asked.owners.at(-1)?.has("page")).toBe(false);
    });

    it("shows a message, and tries again on request, when the list fails", async () => {
      let fail = true;
      serve({ owners: () => (fail ? problem(500, "internal_error") : HttpResponse.json(ownersResponse())) });
      const user = userEvent.setup();
      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      const message = await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      fail = false;
      await user.click(within(message.closest("div")!).getByRole("button", { name: "Tentar novamente" }));

      expect(await screen.findByRole("table", { name: "Donos e os seus números" })).toBeInTheDocument();
    });

    it("shows the API's message for a search that is refused", async () => {
      serve({ owners: () => problem(400, "validation_failed", { search: ["A pesquisa pode ter no máximo 100 caracteres."] }) });

      renderWithProviders(<AdminDashboard initialStats={STATS} />);

      expect(await screen.findByText("A pesquisa pode ter no máximo 100 caracteres.")).toBeInTheDocument();
    });
  });

  it("sends the admin to sign in, coming back to the same view, when the session is over", async () => {
    resetNavigation("/admin?sort=clubs");
    serve({ owners: () => problem(401, "unauthorized") });
    server.use(http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 401 })));

    renderWithProviders(<AdminDashboard initialStats={STATS} />);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/login?next=%2Fadmin%3Fsort%3Dclubs", undefined));
  });
});
