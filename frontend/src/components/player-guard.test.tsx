import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Account } from "@/features/auth/api";
import { PlayerGuard } from "./player-guard";

const redirect = vi.fn((url: string) => {
  throw new Error(`NEXT_REDIRECT ${url}`);
});
vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirect(url),
}));

let requestedPath: string | null = null;
vi.mock("next/headers", () => ({
  headers: async () => new Headers(requestedPath === null ? {} : { "x-pathname": requestedPath }),
}));

let session: Account | null = null;
vi.mock("@/lib/server/session", () => ({
  getSessionAccount: async () => session,
}));

function accountOf(profile: string): Account {
  return { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rita Jogadora", email: "rita@exemplo.pt", profile };
}

async function renderGuard() {
  render(await PlayerGuard({ fallbackPath: "/bookings", children: <p>As minhas reservas</p> }));
}

describe("PlayerGuard", () => {
  beforeEach(() => {
    redirect.mockClear();
    session = null;
    requestedPath = null;
  });

  it("shows the page to a player", async () => {
    session = accountOf("Player");

    await renderGuard();

    expect(screen.getByText("As minhas reservas")).toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
  });

  it("sends a visitor without a session to the login page, to come back to the page", async () => {
    await expect(renderGuard()).rejects.toThrow("NEXT_REDIRECT /login?next=%2Fbookings");
  });

  it("sends a visitor back to the tab and page they asked for", async () => {
    requestedPath = "/bookings?tab=past&page=2";

    await expect(renderGuard()).rejects.toThrow("NEXT_REDIRECT /login?next=%2Fbookings%3Ftab%3Dpast%26page%3D2");
  });

  it("does not trust a requested path that is not a player's page", async () => {
    requestedPath = "/manage/clubs/new";

    await expect(renderGuard()).rejects.toThrow("NEXT_REDIRECT /login?next=%2F");
  });

  it.each([
    ["Owner", "/manage"],
    ["Admin", "/admin"],
  ])("sends the %s profile to its own area", async (profile, home) => {
    session = accountOf(profile);

    await expect(renderGuard()).rejects.toThrow(`NEXT_REDIRECT ${home}`);
  });
});
