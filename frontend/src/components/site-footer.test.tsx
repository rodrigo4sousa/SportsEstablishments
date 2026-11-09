import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SiteFooter } from "./site-footer";

describe("SiteFooter", () => {
  it("is a footer landmark with a named navigation to both legal pages", () => {
    render(<SiteFooter />);

    const navigation = within(screen.getByRole("contentinfo")).getByRole("navigation", { name: "Informação legal" });
    expect(within(navigation).getByRole("link", { name: "Termos e Condições" })).toHaveAttribute("href", "/terms");
    expect(within(navigation).getByRole("link", { name: "Política de Privacidade" })).toHaveAttribute("href", "/privacy");
  });
});
