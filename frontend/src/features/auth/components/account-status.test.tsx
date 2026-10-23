import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { AccountStatus } from "./account-status";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));

function unauthorized() {
  return HttpResponse.json({ status: 401, code: "unauthorized", traceId: "t" }, { status: 401 });
}

describe("AccountStatus", () => {
  it("greets the signed-in player by name", async () => {
    server.use(
      http.get("/api/auth/me", () =>
        HttpResponse.json({ id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Ana Silva", email: "ana@exemplo.pt", profile: "Player" }),
      ),
    );

    renderWithProviders(<AccountStatus />);

    expect(await screen.findByText("Ana Silva")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sair" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Criar conta" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Entrar" })).not.toBeInTheDocument();
  });

  it("offers to sign in or create an account when there is no session, after trying to refresh it", async () => {
    let refreshed = false;
    server.use(
      http.get("/api/auth/me", unauthorized),
      http.post("/api/auth/refresh", () => {
        refreshed = true;
        return HttpResponse.json({ status: 401, code: "invalid_refresh_token", traceId: "t" }, { status: 401 });
      }),
    );

    renderWithProviders(<AccountStatus />);

    expect(await screen.findByRole("link", { name: "Criar conta" })).toHaveAttribute("href", "/register");
    expect(screen.getByRole("link", { name: "Entrar" })).toHaveAttribute("href", "/login");
    expect(screen.queryByRole("button", { name: "Sair" })).not.toBeInTheDocument();
    expect(refreshed).toBe(true);
  });
});
