import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { currentUserQueryKey, type Account } from "../api";
import { RegisterForm } from "./register-form";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));

const account: Account = { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Ana Silva", email: "ana@exemplo.pt", profile: "Player" };

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

function field(name: RegExp) {
  return screen.getByLabelText(name);
}

async function fillIn(values: { name?: string; email?: string; password?: string; phone?: string }) {
  const user = userEvent.setup();
  if (values.name !== undefined) await user.type(field(/^nome$/i), values.name);
  if (values.email !== undefined) await user.type(field(/^email$/i), values.email);
  if (values.password !== undefined) await user.type(field(/palavra-passe/i), values.password);
  if (values.phone !== undefined) await user.type(field(/telemóvel/i), values.phone);
  return user;
}

async function submit(user = userEvent.setup()) {
  await user.click(screen.getByRole("button", { name: "Criar conta" }));
}

function expectFieldError(name: RegExp, message: string) {
  const input = field(name);
  expect(input).toHaveAttribute("aria-invalid", "true");
  expect(input).toHaveAccessibleDescription(expect.stringContaining(message));
}

describe("RegisterForm", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  it("shows a message under each invalid field and does not call the API", async () => {
    let requests = 0;
    server.use(
      http.post("/api/auth/register", () => {
        requests++;
        return HttpResponse.json(account, { status: 201 });
      }),
    );
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ email: "ana@", password: "curta", phone: "123" });
    await submit(user);

    expectFieldError(/^nome$/i, "Indica o teu nome.");
    expectFieldError(/^email$/i, "Indica um email válido, por exemplo nome@exemplo.pt.");
    expectFieldError(/palavra-passe/i, "A palavra-passe tem de ter pelo menos 8 caracteres.");
    expectFieldError(/telemóvel/i, "Indica um número de telemóvel válido, com 9 a 15 algarismos.");
    expect(field(/^nome$/i)).toHaveFocus();
    expect(requests).toBe(0);
  });

  it("registers with trimmed values, signs the player in and goes to the home page", async () => {
    let body: unknown;
    server.use(
      http.post("/api/auth/register", async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(account, { status: 201 });
      }),
    );
    const { queryClient } = renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "  Ana Silva ", email: " ana@exemplo.pt ", password: "palavra-passe", phone: "912 345 678" });
    await submit(user);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
    expect(body).toEqual({ name: "Ana Silva", email: "ana@exemplo.pt", password: "palavra-passe", phone: "912345678" });
    expect(queryClient.getQueryData(currentUserQueryKey)).toEqual(account);
    // The form is cleared, so returning to it never shows the password again.
    expect(field(/palavra-passe/i)).toHaveValue("");
    expect(field(/^email$/i)).toHaveValue("");
  });

  it("sends no phone when it is left blank", async () => {
    let body: Record<string, unknown> = {};
    server.use(
      http.post("/api/auth/register", async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(account, { status: 201 });
      }),
    );
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe" });
    await submit(user);

    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(body.phone).toBeNull();
  });

  it("says on the email field that the email is already registered", async () => {
    server.use(http.post("/api/auth/register", () => problem(409, "email_taken")));
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "Ana@Exemplo.pt", password: "palavra-passe" });
    await submit(user);

    await waitFor(() => expectFieldError(/^email$/i, "Já existe uma conta com este email."));
    expect(field(/^email$/i)).toHaveFocus();
    expect(replace).not.toHaveBeenCalled();
  });

  it("puts the API's validation messages on their fields", async () => {
    server.use(
      http.post("/api/auth/register", () =>
        problem(400, "validation_failed", {
          email: ["Indica um email válido, por exemplo nome@exemplo.pt."],
          phone: ["Indica um número de telemóvel válido, com 9 a 15 algarismos."],
        }),
      ),
    );
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe", phone: "912345678" });
    await submit(user);

    await waitFor(() => expectFieldError(/^email$/i, "Indica um email válido, por exemplo nome@exemplo.pt."));
    expectFieldError(/telemóvel/i, "Indica um número de telemóvel válido, com 9 a 15 algarismos.");
    expect(field(/^nome$/i)).not.toHaveAttribute("aria-invalid");
  });

  it.each([
    [500, "internal_error", "Ocorreu um erro inesperado. Tenta novamente mais tarde."],
    [429, "too_many_requests", "Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente."],
    [403, "origin_not_allowed", "O pedido foi recusado. Recarrega a página e tenta novamente."],
  ])("shows a toast for a %i %s and keeps what was typed", async (status, code, message) => {
    server.use(http.post("/api/auth/register", () => problem(status, code)));
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe" });
    await submit(user);

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(field(/^email$/i)).toHaveValue("ana@exemplo.pt");
    expect(replace).not.toHaveBeenCalled();
  });

  it("shows a toast when the server cannot be reached", async () => {
    server.use(http.post("/api/auth/register", () => HttpResponse.error()));
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe" });
    await submit(user);

    expect(await screen.findByText("Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.")).toBeInTheDocument();
  });

  it("disables the button while the account is being created", async () => {
    let release: () => void = () => {};
    server.use(
      http.post("/api/auth/register", async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return HttpResponse.json(account, { status: 201 });
      }),
    );
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe" });
    await submit(user);

    expect(await screen.findByRole("button", { name: "A criar conta…" })).toBeDisabled();
    release();
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
  });
});
