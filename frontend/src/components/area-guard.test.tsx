import { renderWithIntl } from "@/test/intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Account } from "@/features/auth/api";
import { AreaGuard } from "./area-guard";

const redirect = vi.fn((url: string) => {
  throw new Error(`NEXT_REDIRECT ${url}`);
});
vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirect(url),
  useRouter: () => ({ replace: vi.fn() }),
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
  return { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rui Dono", email: "rui@exemplo.pt", profile };
}

async function renderGuard(profile: "Owner" | "Admin") {
  const tree = await AreaGuard({ profile, title: "Área", children: <p>Conteúdo da área</p> });
  renderWithIntl(<QueryClientProvider client={new QueryClient()}>{tree}</QueryClientProvider>);
}

describe("AreaGuard", () => {
  beforeEach(() => {
    redirect.mockClear();
    session = null;
    requestedPath = null;
  });

  it("sends a visitor without a session to the login page, to come back to the area", async () => {
    await expect(renderGuard("Owner")).rejects.toThrow("NEXT_REDIRECT /login?next=%2Fmanage");
    await expect(renderGuard("Admin")).rejects.toThrow("NEXT_REDIRECT /login?next=%2Fadmin");
  });

  it("sends a visitor without a session back to the subpage they asked for", async () => {
    requestedPath = "/manage/clubs/new";

    await expect(renderGuard("Owner")).rejects.toThrow("NEXT_REDIRECT /login?next=%2Fmanage%2Fclubs%2Fnew");
  });

  it("does not trust a requested path outside the area", async () => {
    requestedPath = "/admin/owners";

    await expect(renderGuard("Owner")).rejects.toThrow("NEXT_REDIRECT /login?next=%2Fmanage");
  });

  it.each([
    ["Owner", "Player", "/"],
    ["Owner", "Admin", "/admin"],
    ["Admin", "Player", "/"],
    ["Admin", "Owner", "/manage"],
  ] as const)("sends a %s area visitor with the %s profile to their own area", async (area, profile, home) => {
    session = accountOf(profile);

    await expect(renderGuard(area)).rejects.toThrow(`NEXT_REDIRECT ${home}`);
    expect(screen.queryByText("Conteúdo da área")).not.toBeInTheDocument();
  });

  it("shows the area, with the account name and the way out, to its own profile", async () => {
    session = accountOf("Owner");

    await renderGuard("Owner");

    expect(screen.getByText("Conteúdo da área")).toBeInTheDocument();
    expect(screen.getByText("Rui Dono")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sair" })).toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
  });
});
