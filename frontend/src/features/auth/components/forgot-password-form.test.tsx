import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { FORGOT_PASSWORD_MESSAGE, ForgotPasswordForm } from "./forgot-password-form";

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

/** Answers every request as the API does: 202 and the same body, whoever the email is. Returns the bodies it received. */
function serveAccepted() {
  const bodies: unknown[] = [];
  server.use(
    http.post("/api/auth/password-reset/request", async ({ request }) => {
      bodies.push(await request.json());
      return HttpResponse.json({ message: "Se existir uma conta…" }, { status: 202 });
    }),
  );
  return bodies;
}

async function ask(email: string) {
  const user = userEvent.setup();
  if (email) await user.type(screen.getByLabelText(/^email$/i), email);
  await user.click(screen.getByRole("button", { name: "Enviar ligação" }));
  return user;
}

describe("ForgotPasswordForm", () => {
  it.each(["jogadora@exemplo.pt", "ninguem@exemplo.pt", "admin@exemplo.pt", "  Dono@Exemplo.PT "])(
    "shows the same message for %s, and nothing else about the account",
    async (email) => {
      const bodies = serveAccepted();
      renderWithProviders(<ForgotPasswordForm />);

      await ask(email);

      const status = await screen.findByRole("status");
      expect(status).toHaveTextContent(FORGOT_PASSWORD_MESSAGE);
      expect(FORGOT_PASSWORD_MESSAGE).toBe(
        "Se existir uma conta com este email, vais receber uma ligação para redefinir a palavra-passe. A ligação é válida durante 1 hora.",
      );
      expect(bodies).toEqual([{ email: email.trim() }]);
      expect(screen.queryByLabelText(/^email$/i)).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Voltar a entrar" })).toHaveAttribute("href", "/login");
    },
  );

  it("asks for the email, and refuses a malformed one, without calling the API", async () => {
    const bodies = serveAccepted();
    renderWithProviders(<ForgotPasswordForm />);

    const user = await ask("");
    expect(await screen.findByLabelText(/^email$/i)).toHaveAccessibleDescription("Indica o teu email.");
    await user.type(screen.getByLabelText(/^email$/i), "não-é-um-email");
    await user.click(screen.getByRole("button", { name: "Enviar ligação" }));

    expect(await screen.findByLabelText(/^email$/i)).toHaveAccessibleDescription("Indica um email válido, por exemplo nome@exemplo.pt.");
    expect(bodies).toEqual([]);
  });

  it("shows the API's message for the email when it refuses it", async () => {
    server.use(http.post("/api/auth/password-reset/request", () => problem(400, "validation_failed", { email: ["Indica um email válido."] })));
    renderWithProviders(<ForgotPasswordForm />);

    await ask("a@b.pt");

    expect(await screen.findByLabelText(/^email$/i)).toHaveAccessibleDescription("Indica um email válido.");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("says to wait when there are too many requests, and keeps the form", async () => {
    server.use(http.post("/api/auth/password-reset/request", () => problem(429, "too_many_requests")));
    renderWithProviders(<ForgotPasswordForm />);

    await ask("jogadora@exemplo.pt");

    expect(await screen.findByRole("alert")).toHaveTextContent("Fizeste demasiados pedidos seguidos");
    expect(screen.getByRole("button", { name: "Enviar ligação" })).toBeInTheDocument();
  });

  it("lets the person ask again after the message", async () => {
    const bodies = serveAccepted();
    renderWithProviders(<ForgotPasswordForm />);
    const user = await ask("jogadora@exemplo.pt");
    await screen.findByRole("status");

    await user.click(screen.getByRole("button", { name: "pede outra ligação" }));
    await user.type(screen.getByLabelText(/^email$/i), "outra@exemplo.pt");
    await user.click(screen.getByRole("button", { name: "Enviar ligação" }));

    await screen.findByRole("status");
    expect(bodies).toEqual([{ email: "jogadora@exemplo.pt" }, { email: "outra@exemplo.pt" }]);
  });
});
