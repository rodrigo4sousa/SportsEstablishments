import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import NotFound from "./not-found";

describe("the global not-found page (UX-007)", () => {
  function render() {
    server.use(http.get("/api/auth/me", () => new HttpResponse(null, { status: 401 })), http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 401 })));
    return renderWithProviders(<NotFound />);
  }

  it("says, in pt-PT, that the page does not exist, and offers the way back", () => {
    render();

    expect(screen.getByRole("heading", { level: 1, name: "Página não encontrada" })).toBeInTheDocument();
    expect(screen.getByText("Este endereço não existe ou já não está disponível.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Pesquisar clubes" })).toHaveAttribute("href", "/");
  });

  it("has the site header, with the brand linking home, instead of a bare page", () => {
    render();

    const header = screen.getByRole("banner");
    expect(header).toHaveTextContent("Reservas de Campos");
    expect(screen.getByRole("link", { name: "Reservas de Campos" })).toHaveAttribute("href", "/");
  });

  it("has one main landmark, the target of the skip link", () => {
    render();

    const main = screen.getByRole("main");
    expect(main).toHaveAttribute("id", "main-content");
    expect(screen.getAllByRole("main")).toHaveLength(1);
  });

  it("has none of Next.js's English default text", () => {
    const { container } = render();

    expect(container.textContent).not.toMatch(/could not be found|404/i);
  });
});
