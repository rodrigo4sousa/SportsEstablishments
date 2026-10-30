import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
    expect(screen.getByRole("link", { name: "As minhas reservas" })).toHaveAttribute("href", "/bookings");
    expect(screen.queryByRole("link", { name: "Criar conta" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Entrar" })).not.toBeInTheDocument();
    // UX-014: the account links are a navigation landmark of their own, signed in or not.
    expect(within(screen.getByRole("navigation", { name: "Conta" })).getByRole("button", { name: "Sair" })).toBeInTheDocument();
  });

  it.each(["Owner", "Admin"])("does not offer the player's bookings to the %s profile", async (profile) => {
    server.use(
      http.get("/api/auth/me", () =>
        HttpResponse.json({ id: "0199f7a0-0000-7000-8000-0000000000bb", name: "Rui Dono", email: "rui@exemplo.pt", profile }),
      ),
    );

    renderWithProviders(<AccountStatus />);

    expect(await screen.findByText("Rui Dono")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "As minhas reservas" })).not.toBeInTheDocument();
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
    expect(within(screen.getByRole("navigation", { name: "Conta" })).getByRole("link", { name: "Criar conta" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sair" })).not.toBeInTheDocument();
    expect(refreshed).toBe(true);
  });
});

describe("the account menu on a phone (UX-021)", () => {
  async function signIn(profile: "Player" | "Owner" = "Player") {
    server.use(
      http.get("/api/auth/me", () =>
        HttpResponse.json({ id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Marta Gomes", email: "marta@exemplo.pt", profile }),
      ),
    );
    renderWithProviders(<AccountStatus />);
    await screen.findByText("Marta Gomes");
  }

  const menu = () => document.getElementById("account-menu") as HTMLElement;
  const toggle = () => screen.getByRole("button", { name: "Conta" });

  it("is one button, «Conta», that starts closed and controls the panel with the items", async () => {
    await signIn();

    expect(toggle()).toHaveAttribute("aria-expanded", "false");
    expect(toggle()).toHaveAttribute("aria-controls", "account-menu");
    // Hidden below `sm` until it is opened, and a row from `sm` up, with the same items in both: one set of elements, shown by CSS.
    expect(menu().className).toMatch(/(^|\s)hidden(\s|$)/);
    expect(menu().className).toContain("sm:flex");
    expect(toggle().className).toContain("sm:hidden");
    expect(within(menu()).getByRole("link", { name: "As minhas reservas" })).toHaveAttribute("href", "/bookings");
    expect(within(menu()).getByText("Marta Gomes")).toBeInTheDocument();
    expect(within(menu()).getByRole("button", { name: "Sair" })).toBeInTheDocument();
  });

  it("opens with the button and says so", async () => {
    await signIn();

    await userEvent.click(toggle());

    expect(toggle()).toHaveAttribute("aria-expanded", "true");
    expect(menu().className).toMatch(/(^|\s)flex(\s|$)/);
    expect(menu().className).not.toMatch(/(^|\s)hidden(\s|$)/);
  });

  it("opens and closes from the keyboard, and Escape closes it and gives focus back to the button", async () => {
    await signIn();
    toggle().focus();

    await userEvent.keyboard("{Enter}");
    expect(toggle()).toHaveAttribute("aria-expanded", "true");

    await userEvent.tab();
    expect(within(menu()).getByRole("link", { name: "As minhas reservas" })).toHaveFocus();
    await userEvent.keyboard("{Escape}");

    expect(toggle()).toHaveAttribute("aria-expanded", "false");
    expect(toggle()).toHaveFocus();
  });

  it("walks the items with Tab, in order, and closes when focus leaves the last one", async () => {
    await signIn();
    await userEvent.click(toggle());

    await userEvent.tab();
    expect(screen.getByRole("link", { name: "As minhas reservas" })).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByRole("button", { name: "Sair" })).toHaveFocus();
    await userEvent.tab();

    expect(toggle()).toHaveAttribute("aria-expanded", "false");
  });

  it("closes when the bookings link is followed", async () => {
    await signIn();
    await userEvent.click(toggle());

    await userEvent.click(screen.getByRole("link", { name: "As minhas reservas" }));

    expect(toggle()).toHaveAttribute("aria-expanded", "false");
  });

  it("closes on a click elsewhere", async () => {
    await signIn();
    await userEvent.click(toggle());

    await userEvent.click(document.body);

    expect(toggle()).toHaveAttribute("aria-expanded", "false");
  });

  it("keeps the items of the profile: an owner has no link to the player's bookings", async () => {
    await signIn("Owner");

    expect(screen.queryByRole("link", { name: "As minhas reservas" })).not.toBeInTheDocument();
    expect(within(menu()).getByRole("button", { name: "Sair" })).toBeInTheDocument();
  });

  it("makes the button and the items 44px tall on a phone", async () => {
    await signIn();

    expect(toggle().className).toContain("h-11");
    expect(screen.getByRole("link", { name: "As minhas reservas" }).className).toContain("min-h-11");
    expect(screen.getByRole("button", { name: "Sair" }).className).toContain("h-11");
  });

  it("has no menu for a visitor: «Entrar» and «Criar conta» fit beside the brand", async () => {
    server.use(http.get("/api/auth/me", unauthorized), http.post("/api/auth/refresh", unauthorized));
    renderWithProviders(<AccountStatus />);

    await screen.findByRole("link", { name: "Criar conta" });

    expect(screen.queryByRole("button", { name: "Conta" })).not.toBeInTheDocument();
  });
});
