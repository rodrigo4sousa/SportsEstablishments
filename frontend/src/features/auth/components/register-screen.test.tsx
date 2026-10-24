import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { resetNavigation } from "@/test/navigation";
import { renderWithProviders } from "@/test/render";
import { RegisterScreen } from "./register-screen";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const next = "/clubs/clube-da-foz?pitch=p1&date=2026-05-04&slot=2026-05-04T10%3A00%3A00Z";

describe("RegisterScreen", () => {
  beforeEach(() => {
    resetNavigation("/register");
  });

  it("shows the sign-up form and a plain way back to signing in when there is no next", () => {
    renderWithProviders(<RegisterScreen />);

    expect(screen.getByRole("form", { name: "Criar conta" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Entrar" })).toHaveAttribute("href", "/login");
  });

  it("keeps next on the way back to signing in", () => {
    resetNavigation(`/register?next=${encodeURIComponent(next)}`);

    renderWithProviders(<RegisterScreen />);

    expect(screen.getByRole("link", { name: "Entrar" }).getAttribute("href")).toBe(`/login?next=${encodeURIComponent(next)}`);
  });
});
