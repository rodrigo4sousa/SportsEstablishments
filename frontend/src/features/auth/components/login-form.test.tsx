import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { currentUserQueryKey, type Account } from "../api";
import { LoginForm } from "./login-form";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));

function accountOf(profile: Account["profile"]): Account {
  return { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Ana Silva", email: "ana@exemplo.pt", profile };
}

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

async function signIn(email = "ana@exemplo.pt", password = "palavra-passe") {
  const user = userEvent.setup();
  if (email) await user.type(screen.getByLabelText(/^email$/i), email);
  if (password) await user.type(screen.getByLabelText(/palavra-passe/i), password);
  await user.click(screen.getByRole("button", { name: "Entrar" }));
  return user;
}

describe("LoginForm", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  it("asks for the missing fields, one message each, and does not call the API", async () => {
    let requests = 0;
    server.use(
      http.post("/api/auth/login", () => {
        requests++;
        return HttpResponse.json(accountOf("Player"));
      }),
    );
    renderWithProviders(<LoginForm next={null} />);

    await signIn("", "");

    expect(await screen.findByLabelText(/^email$/i)).toHaveAccessibleDescription("Indica o teu email.");
    expect(screen.getByLabelText(/palavra-passe/i)).toHaveAccessibleDescription("Indica a palavra-passe.");
    expect(requests).toBe(0);
  });

  it.each([
    ["Player", null, "/"],
    ["Owner", null, "/manage"],
    ["Admin", null, "/admin"],
    ["Owner", "/manage", "/manage"],
    ["Admin", "/admin", "/admin"],
    ["Owner", "/manage/clubs/new", "/manage/clubs/new"],
    ["Owner", "/manage/clubs/0199f7a0-0000-7000-8000-0000000000aa?tab=a", "/manage/clubs/0199f7a0-0000-7000-8000-0000000000aa?tab=a"],
  ] as const)("a %s signing in with next=%s goes to %s", async (profile, next, destination) => {
    let body: unknown;
    server.use(
      http.post("/api/auth/login", async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(accountOf(profile));
      }),
    );
    const { queryClient } = renderWithProviders(<LoginForm next={next} />);

    await signIn("  ana@exemplo.pt ", " palavra-passe");

    await waitFor(() => expect(replace).toHaveBeenCalledWith(destination));
    // The email is trimmed; the password goes exactly as typed.
    expect(body).toEqual({ email: "ana@exemplo.pt", password: " palavra-passe" });
    expect(queryClient.getQueryData(currentUserQueryKey)).toEqual(accountOf(profile));
  });

  it.each([
    ["Owner", "/admin"],
    ["Owner", "//evil.example"],
    ["Owner", "https://evil.example/manage"],
    ["Admin", "/manage"],
    ["Player", "/manage"],
    ["Player", "/login"],
  ] as const)("a %s ignores the unsafe next=%s and goes home", async (profile, next) => {
    server.use(http.post("/api/auth/login", () => HttpResponse.json(accountOf(profile))));
    renderWithProviders(<LoginForm next={next} />);

    await signIn();

    const home = { Player: "/", Owner: "/manage", Admin: "/admin" }[profile];
    await waitFor(() => expect(replace).toHaveBeenCalledWith(home));
  });

  it("shows the one generic message for a 401, clears the password and stays on the page", async () => {
    server.use(http.post("/api/auth/login", () => problem(401, "invalid_credentials")));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    expect(await screen.findByRole("alert")).toHaveTextContent("Email ou palavra-passe incorretos.");
    expect(screen.getByLabelText(/palavra-passe/i)).toHaveValue("");
    expect(screen.getByLabelText(/^email$/i)).toHaveValue("ana@exemplo.pt");
    expect(replace).not.toHaveBeenCalled();
  });

  it("puts focus back on the password field after a refusal, so the next try needs no search (UX-002)", async () => {
    server.use(http.post("/api/auth/login", () => problem(401, "invalid_credentials")));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    await screen.findByRole("alert");
    expect(screen.getByLabelText(/palavra-passe/i)).toHaveFocus();
  });

  it("shows a toast for a 500, with no message inside the form (UX-019)", async () => {
    server.use(http.post("/api/auth/login", () => problem(500, "internal_error")));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows the too-many-requests message for a 429", async () => {
    server.use(http.post("/api/auth/login", () => problem(429, "too_many_requests")));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    expect(await screen.findByRole("alert")).toHaveTextContent("Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.");
    expect(replace).not.toHaveBeenCalled();
  });

  it("picks the message by code, not by the response text", async () => {
    server.use(
      http.post("/api/auth/login", () =>
        HttpResponse.json(
          { status: 401, title: "Texto qualquer", detail: "Outro texto", code: "invalid_credentials", traceId: "t" },
          { status: 401 },
        ),
      ),
    );
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    expect(await screen.findByRole("alert")).toHaveTextContent("Email ou palavra-passe incorretos.");
    expect(screen.queryByText("Texto qualquer")).not.toBeInTheDocument();
  });

  it("puts the API's validation messages on their fields", async () => {
    server.use(http.post("/api/auth/login", () => problem(400, "validation_failed", { email: ["Indica o teu email."] })));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    expect(await screen.findByLabelText(/^email$/i)).toHaveAccessibleDescription("Indica o teu email.");
  });

  it("shows a toast for an unexpected server error", async () => {
    server.use(http.post("/api/auth/login", () => problem(500, "internal_error")));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });

  it("clears the form after signing in, password included", async () => {
    server.use(http.post("/api/auth/login", () => HttpResponse.json(accountOf("Player"))));
    renderWithProviders(<LoginForm next={null} />);

    await signIn();

    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(screen.getByLabelText(/palavra-passe/i)).toHaveValue("");
    expect(screen.getByLabelText(/^email$/i)).toHaveValue("");
  });
});
