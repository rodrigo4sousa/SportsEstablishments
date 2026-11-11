import { renderWithIntl } from "@/test/intl";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NoClubsYet } from "./no-clubs-yet";

describe("NoClubsYet", () => {
  it("invites the owner to create the first club with an active link to the create form", () => {
    renderWithIntl(<NoClubsYet />);

    expect(screen.getByRole("heading", { name: "Ainda não tens nenhum clube" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Criar o primeiro clube" })).toHaveAttribute("href", "/manage/clubs/new");
    expect(screen.queryByText(/em breve/i)).not.toBeInTheDocument();
  });
});
