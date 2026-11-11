import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { LEGAL_VERSION } from "@/features/legal/legal-info";
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

const TERMS_LABEL = "Li e aceito os Termos e Condições e tomei conhecimento da Política de Privacidade";
const TERMS_ERROR = "Para criar conta, tens de aceitar os Termos e Condições e tomar conhecimento da Política de Privacidade.";

function termsBox() {
  return screen.getByRole("checkbox", { name: TERMS_LABEL });
}

async function fillIn(values: { name?: string; email?: string; password?: string; phone?: string; terms?: boolean }) {
  const user = userEvent.setup();
  if (values.name !== undefined) await user.type(field(/^nome$/i), values.name);
  if (values.email !== undefined) await user.type(field(/^email$/i), values.email);
  if (values.password !== undefined) await user.type(field(/palavra-passe/i), values.password);
  if (values.phone !== undefined) await user.type(field(/telemóvel/i), values.phone);
  if (values.terms !== false) await user.click(termsBox());
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

    const user = await fillIn({ email: "ana@", password: "curta", phone: "123", terms: false });
    await submit(user);

    expectFieldError(/^nome$/i, "Indica o teu nome.");
    expectFieldError(/^email$/i, "Indica um email válido, por exemplo nome@exemplo.pt.");
    expectFieldError(/palavra-passe/i, "A palavra-passe tem de ter pelo menos 8 caracteres.");
    expectFieldError(/telemóvel/i, "Indica um número de telemóvel válido, com 9 a 15 algarismos.");
    expect(termsBox()).toHaveAttribute("aria-invalid", "true");
    expect(termsBox()).toHaveAccessibleDescription(expect.stringContaining(TERMS_ERROR));
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
    expect(body).toEqual({ name: "Ana Silva", email: "ana@exemplo.pt", password: "palavra-passe", phone: "912345678", acceptedTermsVersion: LEGAL_VERSION, preferredLanguage: "pt-PT" });
    expect(queryClient.getQueryData(currentUserQueryKey)).toEqual(account);
    // The form is cleared, so returning to it never shows the password again.
    expect(field(/palavra-passe/i)).toHaveValue("");
    expect(field(/^email$/i)).toHaveValue("");
  });

  it("does not call the API until the terms are accepted, and the box takes the focus", async () => {
    let requests = 0;
    server.use(
      http.post("/api/auth/register", () => {
        requests++;
        return HttpResponse.json(account, { status: 201 });
      }),
    );
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe", terms: false });
    await submit(user);

    expect(await screen.findByText(TERMS_ERROR)).toBeInTheDocument();
    expect(termsBox()).toHaveFocus();
    expect(termsBox()).toBeRequired();
    expect(requests).toBe(0);

    await user.keyboard(" ");
    expect(termsBox()).toBeChecked();
    await submit(user);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
    expect(requests).toBe(1);
  });

  it("links to both legal pages from the label, opening them without losing the form", () => {
    renderWithProviders(<RegisterForm />);

    for (const [name, href] of [
      ["Termos e Condições", "/terms"],
      ["Política de Privacidade", "/privacy"],
    ]) {
      const link = screen.getByRole("link", { name });
      expect(link).toHaveAttribute("href", href);
      expect(link).toHaveAttribute("target", "_blank");
    }
  });

  it("shows the API's refusal next to the box", async () => {
    server.use(http.post("/api/auth/register", () => problem(400, "terms_not_accepted", { acceptedTermsVersion: ["recusado"] })));
    renderWithProviders(<RegisterForm />);

    const user = await fillIn({ name: "Ana", email: "ana@exemplo.pt", password: "palavra-passe" });
    await submit(user);

    expect(await screen.findByText(TERMS_ERROR)).toBeInTheDocument();
    expect(termsBox()).toHaveFocus();
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

describe("RegisterForm with a page to return to", () => {
  const club = "/clubs/clube-da-foz?pitch=0199f7a0-0000-7000-8000-0000000000d1&date=2026-05-04&slot=2026-05-04T10%3A00%3A00Z";

  beforeEach(() => {
    replace.mockReset();
    server.use(http.post("/api/auth/register", () => HttpResponse.json(account, { status: 201 })));
  });

  async function registerPlayer() {
    const user = await fillIn({ name: "Ana Silva", email: "ana@exemplo.pt", password: "palavra-passe" });
    await submit(user);
  }

  it("returns a new player to the club page, pitch, date and slot they came from", async () => {
    renderWithProviders(<RegisterForm next={club} />);

    await registerPlayer();

    await waitFor(() => expect(replace).toHaveBeenCalledWith(club));
  });

  it.each([
    ["another site", "https://evil.example/clubs/x"],
    ["a protocol-relative address", "//evil.example/clubs/x"],
    ["a backslash trick", "/\\evil.example/clubs/x"],
    ["the owner area", "/manage"],
    ["the admin area", "/admin"],
    ["the login page", "/login"],
    ["something that is not a path", "clubs/clube-da-foz"],
    ["an empty value", ""],
  ])("ignores %s and goes to the home page", async (_name, next) => {
    renderWithProviders(<RegisterForm next={next} />);

    await registerPlayer();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
  });

  it("goes to the home page when there is no next", async () => {
    renderWithProviders(<RegisterForm next={null} />);

    await registerPlayer();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
  });

  it("does not move to the page when the sign-up fails", async () => {
    server.use(http.post("/api/auth/register", () => problem(409, "email_taken")));
    renderWithProviders(<RegisterForm next={club} />);

    await registerPlayer();

    expectFieldError(/^email$/i, "Já existe uma conta com este email.");
    expect(replace).not.toHaveBeenCalled();
  });
});

describe("RegisterForm for owners", () => {
  const owner: Account = { ...account, id: "0199f7a0-0000-7000-8000-0000000000bb", name: "Rui Dono", email: "rui@exemplo.pt", profile: "Owner" };

  beforeEach(() => {
    replace.mockReset();
  });

  it("registers an owner on the owner endpoint and goes to /manage", async () => {
    let body: Record<string, unknown> = {};
    let playerEndpointCalls = 0;
    server.use(
      http.post("/api/auth/register/owner", async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(owner, { status: 201 });
      }),
      http.post("/api/auth/register", () => {
        playerEndpointCalls++;
        return HttpResponse.json(account, { status: 201 });
      }),
    );
    const { queryClient } = renderWithProviders(<RegisterForm variant="owner" />);

    const user = await fillIn({ name: " Rui Dono ", email: " rui@exemplo.pt ", password: "palavra-passe", phone: "912 345 678" });
    await submit(user);

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/manage"));
    // The same fields as the player form, and no profile: the endpoint decides it.
    expect(body).toEqual({ name: "Rui Dono", email: "rui@exemplo.pt", password: "palavra-passe", phone: "912345678", acceptedTermsVersion: LEGAL_VERSION, preferredLanguage: "pt-PT" });
    expect(playerEndpointCalls).toBe(0);
    expect(queryClient.getQueryData(currentUserQueryKey)).toEqual(owner);
    expect(field(/palavra-passe/i)).toHaveValue("");
  });

  it("is a form of its own for owners and applies the same field rules", async () => {
    let requests = 0;
    server.use(
      http.post("/api/auth/register/owner", () => {
        requests++;
        return HttpResponse.json(owner, { status: 201 });
      }),
    );
    renderWithProviders(<RegisterForm variant="owner" />);

    expect(screen.getByRole("form", { name: "Criar conta de dono" })).toBeInTheDocument();
    const user = await fillIn({ email: "rui@", password: "curta", phone: "123" });
    await submit(user);

    expectFieldError(/^nome$/i, "Indica o teu nome.");
    expectFieldError(/^email$/i, "Indica um email válido, por exemplo nome@exemplo.pt.");
    expectFieldError(/palavra-passe/i, "A palavra-passe tem de ter pelo menos 8 caracteres.");
    expectFieldError(/telemóvel/i, "Indica um número de telemóvel válido, com 9 a 15 algarismos.");
    expect(requests).toBe(0);
  });

  it("says on the email field that the email is already registered, whatever profile has it", async () => {
    server.use(http.post("/api/auth/register/owner", () => problem(409, "email_taken")));
    renderWithProviders(<RegisterForm variant="owner" />);

    const user = await fillIn({ name: "Rui", email: "Rui@Exemplo.pt", password: "palavra-passe" });
    await submit(user);

    await waitFor(() => expectFieldError(/^email$/i, "Já existe uma conta com este email."));
    expect(field(/^email$/i)).toHaveFocus();
    expect(replace).not.toHaveBeenCalled();
  });
});

describe("RegisterForm for owners with a next", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  it("never follows a club page: an owner goes to /manage", async () => {
    server.use(http.post("/api/auth/register/owner", () => HttpResponse.json({ ...account, profile: "Owner" }, { status: 201 })));
    renderWithProviders(<RegisterForm variant="owner" next="/clubs/clube-da-foz?slot=2026-05-04T10%3A00%3A00Z" />);
    const user = await fillIn({ name: "Rui Dono", email: "rui@exemplo.pt", password: "palavra-passe" });

    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/manage"));
  });
});
