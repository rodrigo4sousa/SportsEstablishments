import { renderWithIntl } from "@/test/intl";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PublicClubNotFound } from "./public-club-not-found";

describe("PublicClubNotFound", () => {
  it("tells the visitor the club is not there, without saying why, and offers the search", () => {
    renderWithIntl(<PublicClubNotFound />);

    expect(screen.getByRole("heading", { name: "Clube não encontrado" })).toBeInTheDocument();
    expect(screen.getByText("Este clube não existe ou já não está disponível.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Pesquisar clubes" })).toHaveAttribute("href", "/");
  });
});
