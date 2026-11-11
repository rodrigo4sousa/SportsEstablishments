import { renderWithIntl } from "@/test/intl";
import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/features/catalog/components/club-search", () => ({ ClubSearch: () => <div data-testid="search">pesquisa</div> }));

const { default: Page } = await import("./page");

describe("the home page", () => {
  it("says what the site is, in two plain sentences, above the search (UX-030)", () => {
    renderWithIntl(<Page />);

    const intro = screen.getByText("Pesquisa clubes de futsal, futebol 7, padel e ténis, vê os horários livres e reserva o teu campo.");
    expect(intro).toBeInTheDocument();
    expect(screen.getByText(/Podes pesquisar sem conta; para reservar precisas de uma conta de jogador\./)).toBeInTheDocument();
    expect(Boolean(intro.compareDocumentPosition(screen.getByTestId("search")) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true);
  });

  it("keeps the title as the one h1, with the intro right under it", () => {
    renderWithIntl(<Page />);

    const title = screen.getByRole("heading", { level: 1, name: "Encontra onde jogar" });
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(Boolean(title.compareDocumentPosition(screen.getByText(/Pesquisa clubes de futsal/)) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true);
  });

  it("offers owners the way to sign up", () => {
    renderWithIntl(<Page />);

    expect(screen.getByRole("link", { name: "Cria uma conta de dono" })).toHaveAttribute("href", "/register/owner");
  });
});
