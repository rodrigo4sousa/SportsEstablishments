import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fireEvent } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { OwnerStatistics, defaultRange } from "./owner-statistics";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const FOZ = "0199f7a0-0000-7000-8000-0000000000c1";
const MAIA = "0199f7a0-0000-7000-8000-0000000000c2";

const figures = (patch: Record<string, number> = {}) => ({ bookedHours: 4.5, openHours: 12, occupation: 0.375, revenue: 100, confirmedBookings: 4, noShows: 1, ...patch });

function ownerResponse(clubs: Record<string, unknown>[] = [{ clubId: FOZ, clubName: "Clube da Foz", ...figures() }, { clubId: MAIA, clubName: "Padel Maia", ...figures({ bookedHours: 0, openHours: 0, occupation: 0, revenue: 0, confirmedBookings: 0, noShows: 0 }) }], total = figures()) {
  return { from: "2026-05-04", to: "2026-05-10", clubs, total };
}

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) }, { status, headers: { "Content-Type": "application/problem+json" } });
}

function serve(respond: (query: URLSearchParams) => Response = () => HttpResponse.json(ownerResponse())) {
  const asked: URLSearchParams[] = [];
  server.use(
    http.get("/api/manage/statistics", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.push(query);
      return respond(query);
    }),
  );
  return asked;
}

const text = (value: string | null) => (value ?? "").replace(/\s/g, " ");
const table = () => screen.getByRole("table", { name: "Ocupação e receita por clube" });
const rows = () => within(table()).getAllByRole("row").slice(1);

describe("OwnerStatistics", () => {
  beforeEach(() => {
    resetNavigation("/manage/statistics");
  });

  it("asks for the last 30 days, with no dates, and shows the clubs and the total with pt-PT hours, percentages and euros", async () => {
    const asked = serve();

    renderWithProviders(<OwnerStatistics />);

    await screen.findByRole("table", { name: "Ocupação e receita por clube" });
    expect(asked).toHaveLength(1);
    expect(Object.fromEntries(asked[0])).toEqual({});
    const [foz, maia, total] = rows();
    expect(within(foz).getByRole("rowheader")).toHaveTextContent("Clube da Foz");
    expect(within(foz).getAllByRole("cell").map((cell) => text(cell.textContent))).toEqual(["4,5 h", "12 h", "37,5 %", "100,00 €", "4", "1"]);
    // A club with nothing open or booked is a row of zeros.
    expect(within(maia).getAllByRole("cell").map((cell) => text(cell.textContent))).toEqual(["0 h", "0 h", "0 %", "0,00 €", "0", "0"]);
    expect(within(total).getByRole("rowheader")).toHaveTextContent("Total");
    expect(within(total).getAllByRole("cell").map((cell) => text(cell.textContent))).toEqual(["4,5 h", "12 h", "37,5 %", "100,00 €", "4", "1"]);
    expect(screen.getByText("De 04/05/2026 a 10/05/2026.")).toBeInTheDocument();
  });

  it("shows the default period in the date fields until one is chosen", async () => {
    serve();

    renderWithProviders(<OwnerStatistics />);

    await screen.findByRole("table", { name: "Ocupação e receita por clube" });
    expect(screen.getByLabelText("De")).toHaveValue("2026-05-04");
    expect(screen.getByLabelText("Até")).toHaveValue("2026-05-10");
    expect(screen.getByRole("button", { name: "Últimos 30 dias" })).toBeDisabled();
  });

  it("asks again with the dates chosen, writes them in the URL and offers the way back to the last 30 days", async () => {
    const asked = serve();
    const user = userEvent.setup();
    renderWithProviders(<OwnerStatistics />);
    await screen.findByRole("table", { name: "Ocupação e receita por clube" });

    fireEvent.change(screen.getByLabelText("De"), { target: { value: "2026-04-01" } });
    await waitFor(() => expect(asked.at(-1)?.get("from")).toBe("2026-04-01"));
    fireEvent.change(screen.getByLabelText("Até"), { target: { value: "2026-04-30" } });
    await waitFor(() => expect(Object.fromEntries(asked.at(-1)!)).toEqual({ from: "2026-04-01", to: "2026-04-30" }));
    expect(currentPath()).toBe("/manage/statistics?from=2026-04-01&to=2026-04-30");

    await user.click(screen.getByRole("button", { name: "Últimos 30 dias" }));

    await waitFor(() => expect(Object.fromEntries(asked.at(-1)!)).toEqual({}));
    expect(currentPath()).toBe("/manage/statistics");
  });

  it("reads the period from the URL", async () => {
    resetNavigation("/manage/statistics?from=2026-05-04&to=2026-05-10");
    const asked = serve();
    renderWithProviders(<OwnerStatistics />);

    await screen.findByRole("table", { name: "Ocupação e receita por clube" });

    expect(Object.fromEntries(asked[0])).toEqual({ from: "2026-05-04", to: "2026-05-10" });
    expect(screen.getByLabelText("De")).toHaveValue("2026-05-04");
  });

  it("ignores dates in the URL that are not dates", async () => {
    resetNavigation("/manage/statistics?from=ontem&to=2026-13-01");
    const asked = serve();
    renderWithProviders(<OwnerStatistics />);

    await screen.findByRole("table", { name: "Ocupação e receita por clube" });

    expect(Object.fromEntries(asked[0])).toEqual({});
  });

  it("links each club to its own statistics for the same period", async () => {
    resetNavigation("/manage/statistics?from=2026-05-04&to=2026-05-10");
    serve();
    renderWithProviders(<OwnerStatistics />);

    const link = await within(await screen.findByRole("table", { name: "Ocupação e receita por clube" })).findByRole("link", { name: "Clube da Foz" });

    expect(link).toHaveAttribute("href", `/manage/clubs/${FOZ}/statistics?from=2026-05-04&to=2026-05-10`);
  });

  it("says there are no clubs yet, and how to make one", async () => {
    serve(() => HttpResponse.json(ownerResponse([], figures({ bookedHours: 0, openHours: 0, occupation: 0, revenue: 0, confirmedBookings: 0, noShows: 0 }))));

    renderWithProviders(<OwnerStatistics />);

    expect(await screen.findByText(/Ainda não tens clubes/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Criar o primeiro clube" })).toHaveAttribute("href", "/manage/clubs/new");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("shows its own message, never the API's text, for a refused period", async () => {
    serve(() => problem(400, "validation_failed", { to: ["A data final não pode ser anterior à inicial."] }));

    renderWithProviders(<OwnerStatistics />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Este período não é aceite. Verifica as datas: «De» não pode ser depois de «Até», as datas vão de 2000 a 2100 e o período tem no máximo 366 dias.");
  });

  it("shows a message and tries again on request when the numbers cannot be loaded", async () => {
    let fail = true;
    serve(() => (fail ? problem(500, "internal_error") : HttpResponse.json(ownerResponse())));
    const user = userEvent.setup();
    renderWithProviders(<OwnerStatistics />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    fail = false;
    await user.click(within(alert).getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("table", { name: "Ocupação e receita por clube" })).toBeInTheDocument();
  });

  it("sends the owner to sign in, coming back to the same period, when the session is over", async () => {
    resetNavigation("/manage/statistics?from=2026-05-04&to=2026-05-10");
    serve(() => problem(401, "unauthorized"));
    server.use(http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 401 })));

    renderWithProviders(<OwnerStatistics />);

    await waitFor(() => expect(replace).toHaveBeenCalledWith(`/login?next=${encodeURIComponent("/manage/statistics?from=2026-05-04&to=2026-05-10")}`, undefined));
  });

  it("the default range helper is the last 30 days", () => {
    expect(defaultRange(new Date("2026-05-13T10:00:00Z")).to).toBe("2026-05-13");
  });

  it("keeps every figure, revenue included, as a labelled cell, so a phone can stack them instead of scrolling sideways (UX-006)", async () => {
    serve();
    renderWithProviders(<OwnerStatistics />);

    const row = within(await screen.findByRole("table", { name: "Ocupação e receita por clube" })).getByRole("row", { name: /Clube da Foz/ });

    const labels = within(row)
      .getAllByRole("cell")
      .map((cell) => cell.getAttribute("data-label"));
    expect(labels).toEqual(["Horas reservadas", "Horas abertas", "Ocupação", "Receita", "Confirmadas", "Faltas"]);
    // From sm up it is a table; below, the rows are blocks (cards), and the scrolling region around it is still named.
    expect(table()).toHaveClass("max-sm:block");
    expect(row).toHaveClass("max-sm:block", "max-sm:border");
    expect(screen.getByRole("region", { name: "Ocupação e receita por clube" })).toHaveAttribute("tabindex", "0");
  });

  it("shows a skeleton with one status message while it loads", () => {
    serve();
    renderWithProviders(<OwnerStatistics />);

    expect(screen.getByRole("status")).toHaveTextContent("A carregar as estatísticas…");
  });
});
