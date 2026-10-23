import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Account } from "@/features/auth/api";
import { GuestOnly } from "./guest-only";

const redirect = vi.fn((url: string) => {
  throw new Error(`NEXT_REDIRECT ${url}`);
});
vi.mock("next/navigation", () => ({ redirect: (url: string) => redirect(url) }));

let session: Account | null = null;
vi.mock("@/lib/server/session", () => ({ getSessionAccount: async () => session }));

function accountOf(profile: string): Account {
  return { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rui", email: "rui@exemplo.pt", profile };
}

describe("GuestOnly", () => {
  beforeEach(() => {
    redirect.mockClear();
    session = null;
  });

  it("shows the page to a visitor without a session", async () => {
    render(await GuestOnly({ children: <p>Formulário</p> }));

    expect(screen.getByText("Formulário")).toBeInTheDocument();
    expect(redirect).not.toHaveBeenCalled();
  });

  it.each([
    ["Player", "/"],
    ["Owner", "/manage"],
    ["Admin", "/admin"],
  ])("sends a signed-in %s to %s without showing the page", async (profile, home) => {
    session = accountOf(profile);

    await expect(GuestOnly({ children: <p>Formulário</p> })).rejects.toThrow(`NEXT_REDIRECT ${home}`);
    expect(screen.queryByText("Formulário")).not.toBeInTheDocument();
  });
});
