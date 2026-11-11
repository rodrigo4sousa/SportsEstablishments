import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ClubStatistics } from "./club-statistics";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const CLUB = "0199f7a0-0000-7000-8000-0000000000c1";
const PADEL = "0199f7a0-0000-7000-8000-0000000000d1";
const TENNIS = "0199f7a0-0000-7000-8000-0000000000d2";

const figures = (patch: Record<string, number> = {}) => ({ bookedHours: 3, openHours: 4, occupation: 0.75, revenue: 80, confirmedBookings: 3, noShows: 1, ...patch });

const padel = { pitchId: PADEL, name: "Padel 1", sport: "padel", status: "active", ...figures() };
const tennis = { pitchId: TENNIS, name: "Ténis 1", sport: "tennis", status: "inactive", ...figures({ bookedHours: 1.5, openHours: 0, occupation: 0, revenue: 20, confirmedBookings: 1, noShows: 0 }) };

function clubResponse(pitches: Record<string, unknown>[] = [padel, tennis], total = figures({ bookedHours: 4.5, openHours: 4, occupation: 1.125, revenue: 100, confirmedBookings: 4, noShows: 1 })) {
  return { clubId: CLUB, clubName: "Clube da Foz", from: "2026-05-04", to: "2026-05-10", pitches, total };
}

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) }, { status, headers: { "Content-Type": "application/problem+json" } });
}

/** Serves the club like the API: all pitches, or the one asked for. Returns the queries it received. */
function serve(respond?: (query: URLSearchParams) => Response) {
  const asked: URLSearchParams[] = [];
  server.use(
    http.get(`/api/manage/clubs/${CLUB}/statistics`, ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.push(query);
      if (respond) return respond(query);
      const pitch = query.get("pitchId");
      return HttpResponse.json(pitch ? clubResponse([padel, tennis].filter((candidate) => candidate.pitchId === pitch), figures()) : clubResponse());
    }),
  );
  return asked;
}

const text = (value: string | null) => (value ?? "").replace(/\s/g, " ");
const table = () => screen.getByRole("table", { name: "Ocupação e receita por campo" });
const rows = () => within(table()).getAllByRole("row").slice(1);

describe("ClubStatistics", () => {
  beforeEach(() => {
    resetNavigation(`/manage/clubs/${CLUB}/statistics`);
  });

  it("shows each pitch and the total, with pt-PT hours, percentages and euros, and says an inactive pitch is inactive", async () => {
    const asked = serve();

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    await screen.findByRole("table", { name: "Ocupação e receita por campo" });
    expect(asked).toHaveLength(1);
    expect(Object.fromEntries(asked[0])).toEqual({});
    const [first, second, total] = rows();
    expect(within(first).getByRole("rowheader")).toHaveTextContent("Padel 1");
    expect(within(first).getByRole("rowheader")).toHaveTextContent("Padel");
    expect(within(first).getAllByRole("cell").map((cell) => text(cell.textContent))).toEqual(["3 h", "4 h", "75 %", "80,00 €", "3", "1"]);
    expect(within(second).getByRole("rowheader")).toHaveTextContent("Ténis 1");
    expect(within(second).getByRole("rowheader")).toHaveTextContent("inativo");
    expect(within(second).getAllByRole("cell").map((cell) => text(cell.textContent))).toEqual(["1,5 h", "0 h", "0 %", "20,00 €", "1", "0"]);
    expect(within(total).getAllByRole("cell").map((cell) => text(cell.textContent))).toEqual(["4,5 h", "4 h", "112,5 %", "100,00 €", "4", "1"]);
    expect(screen.getByText("Clube da Foz, de 04/05/2026 a 10/05/2026.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "← Voltar ao clube" })).toHaveAttribute("href", `/manage/clubs/${CLUB}`);
  });

  it("filters by pitch: asks again with the pitch, writes it in the URL, and keeps every pitch in the list", async () => {
    const asked = serve();
    const user = userEvent.setup();
    renderWithProviders(<ClubStatistics clubId={CLUB} />);
    await screen.findByRole("table", { name: "Ocupação e receita por campo" });
    const select = screen.getByLabelText("Campo");
    expect(within(select).getAllByRole("option").map((option) => option.textContent)).toEqual(["Todos os campos", "Padel 1", "Ténis 1"]);

    await user.selectOptions(select, "Padel 1");

    await waitFor(() => expect(asked.some((query) => query.get("pitchId") === PADEL)).toBe(true));
    expect(currentPath()).toBe(`/manage/clubs/${CLUB}/statistics?pitch=${PADEL}`);
    await waitFor(() => expect(rows()).toHaveLength(2)); // the pitch and the total
    expect(within(rows()[0]).getByRole("rowheader")).toHaveTextContent("Padel 1");
    // The choices did not shrink to the one pitch.
    expect(within(screen.getByLabelText("Campo")).getAllByRole("option")).toHaveLength(3);
    expect(screen.getByLabelText("Campo")).toHaveValue(PADEL);

    await user.selectOptions(screen.getByLabelText("Campo"), "Todos os campos");

    await waitFor(() => expect(currentPath()).toBe(`/manage/clubs/${CLUB}/statistics`));
    await waitFor(() => expect(rows()).toHaveLength(3));
  });

  it("reads the period and the pitch from the URL and sends them", async () => {
    resetNavigation(`/manage/clubs/${CLUB}/statistics?from=2026-05-04&to=2026-05-10&pitch=${TENNIS}`);
    const asked = serve();

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    await screen.findByRole("table", { name: "Ocupação e receita por campo" });
    expect(asked.some((query) => Object.keys(Object.fromEntries(query)).sort().join() === "from,pitchId,to" && query.get("pitchId") === TENNIS)).toBe(true);
    expect(screen.getByLabelText("Campo")).toHaveValue(TENNIS);
    expect(screen.getByLabelText("De")).toHaveValue("2026-05-04");
  });

  it("keeps the pitch when the period changes, and the period when the pitch changes", async () => {
    resetNavigation(`/manage/clubs/${CLUB}/statistics?pitch=${PADEL}`);
    serve();
    const user = userEvent.setup();
    renderWithProviders(<ClubStatistics clubId={CLUB} />);
    await screen.findByRole("table", { name: "Ocupação e receita por campo" });

    fireEvent.change(screen.getByLabelText("De"), { target: { value: "2026-05-01" } });
    await waitFor(() => expect(currentPath()).toBe(`/manage/clubs/${CLUB}/statistics?from=2026-05-01&pitch=${PADEL}`));
    await user.selectOptions(screen.getByLabelText("Campo"), "Ténis 1");

    await waitFor(() => expect(currentPath()).toBe(`/manage/clubs/${CLUB}/statistics?from=2026-05-01&pitch=${TENNIS}`));
  });

  it("says a club without pitches that were open or booked has nothing to show", async () => {
    serve(() => HttpResponse.json(clubResponse([], figures({ bookedHours: 0, openHours: 0, occupation: 0, revenue: 0, confirmedBookings: 0, noShows: 0 }))));

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    expect(await screen.findByText(/não tem campos com horas abertas nem reservas neste período/)).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("is the same not-found page for a club that is missing or another owner's", async () => {
    serve(() => problem(404, "club_not_found"));

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    expect(await screen.findByText("Este clube não existe ou não é teu.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Voltar aos clubes" })).toHaveAttribute("href", "/manage");
  });

  it("says a pitch that is not of the club does not exist, and offers all of them", async () => {
    resetNavigation(`/manage/clubs/${CLUB}/statistics?pitch=${PADEL}`);
    const user = userEvent.setup();
    serve((query) => (query.get("pitchId") ? problem(404, "pitch_not_found") : HttpResponse.json(clubResponse())));

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    expect(await screen.findByText("Este campo já não existe neste clube.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Ver todos os campos" }));
    await waitFor(() => expect(currentPath()).toBe(`/manage/clubs/${CLUB}/statistics`));
  });

  it("shows its own message, never the API's text, for a refused period", async () => {
    serve(() => problem(400, "validation_failed", { to: ["O período pode ter no máximo 366 dias."] }));

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Este período não é aceite. Verifica as datas: «De» não pode ser depois de «Até», as datas vão de 2000 a 2100 e o período tem no máximo 366 dias.");
  });

  it("shows a message, and tries again on request, for any other failure", async () => {
    let fail = true;
    serve(() => (fail ? problem(500, "internal_error") : HttpResponse.json(clubResponse())));
    const user = userEvent.setup();
    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    fail = false;
    await user.click(within(alert).getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("table", { name: "Ocupação e receita por campo" })).toBeInTheDocument();
  });

  it("sends the owner to sign in, coming back to the same view, when the session is over", async () => {
    resetNavigation(`/manage/clubs/${CLUB}/statistics?from=2026-05-04&pitch=${PADEL}`);
    serve(() => problem(401, "unauthorized"));
    server.use(http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 401 })));

    renderWithProviders(<ClubStatistics clubId={CLUB} />);

    await waitFor(() =>
      expect(replace).toHaveBeenCalledWith(`/login?next=${encodeURIComponent(`/manage/clubs/${CLUB}/statistics?from=2026-05-04&pitch=${PADEL}`)}`, undefined),
    );
  });
});
