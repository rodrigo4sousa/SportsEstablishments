import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { currentUserQueryKey, type Account } from "../api";
import { LoginScreen } from "./login-screen";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
  useSearchParams: () => new URLSearchParams(search),
}));

function accountOf(profile: Account["profile"]): Account {
  return { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rui Dono", email: "rui@exemplo.pt", profile };
}

function unauthorized(code = "invalid_refresh_token") {
  return HttpResponse.json({ status: 401, title: "Erro", code, traceId: "t" }, { status: 401 });
}

describe("LoginScreen (silent refresh)", () => {
  beforeEach(() => {
    replace.mockReset();
    search = "";
  });

  it("continues to next without showing the form when the silent refresh works", async () => {
    search = "next=%2Fmanage";
    server.use(
      http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 204 })),
      http.get("/api/auth/me", () => HttpResponse.json(accountOf("Owner"))),
    );

    const { queryClient } = renderWithProviders(<LoginScreen />);

    expect(screen.getByRole("status")).toHaveTextContent("A verificar a sessão");
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/manage"));
    expect(screen.queryByRole("button", { name: "Entrar" })).not.toBeInTheDocument();
    expect(queryClient.getQueryData(currentUserQueryKey)).toEqual(accountOf("Owner"));
  });

  it("goes to the profile's home when next is missing or not the user's area", async () => {
    search = "next=%2Fadmin";
    server.use(
      http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 204 })),
      http.get("/api/auth/me", () => HttpResponse.json(accountOf("Owner"))),
    );

    renderWithProviders(<LoginScreen />);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/manage"));
  });

  it("shows the form when the silent refresh fails", async () => {
    server.use(http.post("/api/auth/refresh", () => unauthorized()));

    renderWithProviders(<LoginScreen />);

    expect(await screen.findByRole("button", { name: "Entrar" })).toBeInTheDocument();
    expect(screen.getByLabelText(/^email$/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Criar conta" })).toHaveAttribute("href", "/register");
    expect(screen.getByRole("link", { name: "Esqueci-me da palavra-passe" })).toHaveAttribute("href", "/forgot-password");
    expect(replace).not.toHaveBeenCalled();
  });

  it("keeps next on the link to the sign-up page, so creating an account returns to the same place", async () => {
    const next = "/clubs/clube-da-foz?pitch=p1&date=2026-05-04&slot=2026-05-04T10%3A00%3A00Z";
    search = `next=${encodeURIComponent(next)}`;
    server.use(http.post("/api/auth/refresh", () => unauthorized()));

    renderWithProviders(<LoginScreen />);

    const link = await screen.findByRole("link", { name: "Criar conta" });
    expect(link.getAttribute("href")).toBe(`/register?next=${encodeURIComponent(next)}`);
  });

  it("shows the form when the server cannot be reached", async () => {
    server.use(http.post("/api/auth/refresh", () => HttpResponse.error()));

    renderWithProviders(<LoginScreen />);

    expect(await screen.findByRole("button", { name: "Entrar" })).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it("shows the form when the refresh works but the account cannot be read", async () => {
    server.use(
      http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 204 })),
      http.get("/api/auth/me", () => unauthorized("unauthorized")),
    );

    renderWithProviders(<LoginScreen />);

    expect(await screen.findByRole("button", { name: "Entrar" })).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});
