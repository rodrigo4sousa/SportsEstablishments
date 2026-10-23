import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { NoClubsYet } from "./no-clubs-yet";

describe("NoClubsYet", () => {
  it("invites the owner to create the first club, marking the action as not available yet", () => {
    render(<NoClubsYet />);

    expect(screen.getByRole("heading", { name: "Ainda não tens nenhum clube" })).toBeInTheDocument();
    const action = screen.getByRole("button", { name: "Criar o primeiro clube" });
    expect(action).toBeDisabled();
    expect(action).toHaveAccessibleDescription("Em breve: a criação de clubes ainda não está disponível.");
  });
});
