import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { PublicClub } from "../types";
import { ClubDetails } from "./club-details";

const club: PublicClub = {
  slug: "clube-da-foz",
  name: "Clube da Foz",
  address: "Rua do Mar 1",
  locality: "Porto",
  phone: "+351912345678",
  contactEmail: "geral@foz.pt",
  pitches: [
    { id: "p1", name: "Padel 1", sport: "padel", pricePerSlot: 18.5, hasPriceBands: false },
    { id: "p2", name: "Ténis 1", sport: "tennis", pricePerSlot: 12, hasPriceBands: false },
  ],
};

describe("ClubDetails", () => {
  it("shows the contacts, with links to call and to write", () => {
    render(<ClubDetails club={club} />);

    expect(screen.getByText("Rua do Mar 1, Porto")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "+351912345678" })).toHaveAttribute("href", "tel:+351912345678");
    expect(screen.getByRole("link", { name: "geral@foz.pt" })).toHaveAttribute("href", "mailto:geral@foz.pt");
  });

  it("lists the pitches with their sport and price per slot in pt-PT format", () => {
    render(<ClubDetails club={club} />);

    const [padel, tennis] = within(screen.getByRole("list", { name: "Campos" })).getAllByRole("listitem");
    // Intl writes a no-break space before the euro sign.
    expect(padel.textContent?.replace(/\s/g, " ")).toBe("Padel 1 · Padel18,50 € por slot");
    expect(tennis.textContent?.replace(/\s/g, " ")).toBe("Ténis 1 · Ténis12,00 € por slot");
  });

  it("calls the price of a pitch with price bands its base price and says some hours cost something else (US10)", () => {
    render(<ClubDetails club={{ ...club, pitches: [{ ...club.pitches[0], hasPriceBands: true }, club.pitches[1]] }} />);

    const [padel, tennis] = within(screen.getByRole("list", { name: "Campos" })).getAllByRole("listitem");
    expect(padel.textContent?.replace(/\s/g, " ")).toContain("Preço base: 18,50 € por slot");
    expect(within(padel).getByText(/Alguns horários têm outro preço/)).toBeInTheDocument();
    // A pitch without bands says nothing of the kind.
    expect(tennis.textContent?.replace(/\s/g, " ")).toBe("Ténis 1 · Ténis12,00 € por slot");
    expect(within(tennis).queryByText(/Alguns horários têm outro preço/)).not.toBeInTheDocument();
  });

  it("says so when the club has no pitch to show", () => {
    render(<ClubDetails club={{ ...club, pitches: [] }} />);

    expect(screen.getByText("Este clube não tem campos disponíveis de momento.")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Campos" })).not.toBeInTheDocument();
  });
});
