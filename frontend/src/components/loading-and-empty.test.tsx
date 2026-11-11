import { renderWithIntl } from "@/test/intl";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { EmptyState } from "./empty-state";
import { LoadingRegion } from "./loading-region";
import { RouteLoading } from "./route-loading";
import { Skeleton } from "./ui/skeleton";

describe("loading states (UX-020)", () => {
  it("a skeleton is decorative: hidden from assistive technology, and still when motion is reduced", () => {
    const { container } = renderWithIntl(<Skeleton className="h-4 w-10" />);

    const block = container.firstElementChild;
    expect(block).toHaveAttribute("aria-hidden", "true");
    expect(block?.className).toContain("animate-pulse");
    expect(block?.className).toContain("motion-reduce:animate-none");
  });

  it("a loading region says what is loading, once, in words", () => {
    renderWithIntl(
      <LoadingRegion label="A carregar as reservas…">
        <Skeleton className="h-4" />
        <Skeleton className="h-4" />
      </LoadingRegion>,
    );

    expect(screen.getByRole("status")).toHaveTextContent("A carregar as reservas…");
  });

  it("the route loading page is skeleton blocks with one message", () => {
    const { container } = renderWithIntl(<RouteLoading />);

    expect(screen.getByRole("status")).toHaveTextContent("A carregar…");
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(1);
  });
});

describe("EmptyState", () => {
  it("says what is missing and offers the next step", () => {
    renderWithIntl(<EmptyState title="Não tens reservas próximas." description="Pesquisa um clube." action={<button type="button">Pesquisar clubes</button>} />);

    expect(screen.getByRole("status")).toHaveTextContent("Não tens reservas próximas.");
    expect(screen.getByText("Pesquisa um clube.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pesquisar clubes" })).toBeInTheDocument();
  });

  it("needs only a title", () => {
    renderWithIntl(<EmptyState title="Sem resultados." />);

    expect(screen.getByRole("status")).toHaveTextContent("Sem resultados.");
  });
});
