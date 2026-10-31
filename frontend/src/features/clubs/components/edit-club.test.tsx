import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { EditClub } from "./edit-club";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));

const id = "0199f7a0-0000-7000-8000-0000000000c1";

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

describe("EditClub", () => {
  it("loads the club and shows its edit form", async () => {
    server.use(
      http.get(`/api/manage/clubs/${id}`, () =>
        HttpResponse.json({
          id,
          slug: "clube-de-tenis-da-foz",
          name: "Clube de Ténis da Foz",
          address: "Rua do Mar, 1",
          locality: "Porto",
          phone: "912345678",
          contactEmail: "geral@tenisfoz.pt",
          cancellationWindowHours: 12,
          timeZone: "Europe/Lisbon",
          notifyOnNewBooking: true,
        }),
      ),
    );

    server.use(
      http.get(`/api/manage/clubs/${id}/pitches`, () => HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 })),
    );

    renderWithProviders(<EditClub clubId={id} />);

    expect(screen.getByRole("status")).toHaveTextContent("A carregar o clube");
    expect(await screen.findByLabelText(/^nome$/i)).toHaveValue("Clube de Ténis da Foz");
    expect(screen.getByLabelText(/janela de cancelamento/i)).toHaveValue("12");
    // The club's pitches are managed right below its details.
    expect(await screen.findByRole("heading", { name: "Campos" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Adicionar campo" })).toBeInTheDocument();
    // UX-023, UX-012: the club's details and its pitches are separate sections, and the page says which club it is.
    expect(screen.getByRole("heading", { level: 2, name: "Dados do clube" })).toBeInTheDocument();
    expect(screen.getByText("Clube de Ténis da Foz", { selector: "p" })).toBeInTheDocument();
    expect(document.title).toBe("Editar clube · Clube de Ténis da Foz · Reservas de Campos");
    // US11: the club's calendar is one click away.
    expect(screen.getByRole("link", { name: "Calendário de reservas" })).toHaveAttribute("href", `/manage/clubs/${id}/calendar`);
    // US14: and so are the club's statistics.
    expect(screen.getByRole("link", { name: "Estatísticas" })).toHaveAttribute("href", `/manage/clubs/${id}/statistics`);
  });

  it("shows the not-found page for a club the API answers 404 for (unknown, or another owner's)", async () => {
    let calls = 0;
    server.use(
      http.get(`/api/manage/clubs/${id}`, () => {
        calls++;
        return problem(404, "club_not_found");
      }),
    );

    renderWithProviders(<EditClub clubId={id} />);

    expect(await screen.findByRole("heading", { name: "Clube não encontrado" })).toBeInTheDocument();
    expect(screen.getByText("Este clube não existe ou não é teu.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Voltar aos clubes" })).toHaveAttribute("href", "/manage");
    expect(screen.queryByLabelText(/^nome$/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Campos" })).not.toBeInTheDocument();
    expect(calls).toBe(1);
  });

  it("shows the same page for a 404 without our error code (e.g. an id the routing rejects)", async () => {
    server.use(http.get(`/api/manage/clubs/${id}`, () => problem(404, "not_found")));

    renderWithProviders(<EditClub clubId={id} />);

    expect(await screen.findByRole("heading", { name: "Clube não encontrado" })).toBeInTheDocument();
  });

  it("shows an error, not the not-found page, when the API fails", async () => {
    server.use(http.get(`/api/manage/clubs/${id}`, () => problem(500, "internal_error")));

    renderWithProviders(<EditClub clubId={id} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    expect(screen.queryByRole("heading", { name: "Clube não encontrado" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tentar novamente" })).toBeInTheDocument();
  });
});
