import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { resetNavigation } from "@/test/navigation";
import { ResetPasswordForm } from "./reset-password-form";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const LINK = "/reset-password?email=rita%40exemplo.pt&token=CfDJ8%2Ba%2Fb%3D%3D";

type ConfirmBody = { email: string; token: string; newPassword: string };

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

function serveConfirm(respond: (body: ConfirmBody) => Response = () => new HttpResponse(null, { status: 204 })) {
  const bodies: ConfirmBody[] = [];
  server.use(
    http.post("/api/auth/password-reset/confirm", async ({ request }) => {
      const body = (await request.json()) as ConfirmBody;
      bodies.push(body);
      return respond(body);
    }),
  );
  return bodies;
}

async function submit(password: string, confirmation = password) {
  const user = userEvent.setup();
  if (password) await user.type(screen.getByLabelText("Nova palavra-passe"), password);
  if (confirmation) await user.type(screen.getByLabelText("Repetir a nova palavra-passe"), confirmation);
  await user.click(screen.getByRole("button", { name: "Definir palavra-passe" }));
  return user;
}

describe("ResetPasswordForm", () => {
  beforeEach(() => {
    resetNavigation(LINK);
  });

  it("sends the email and the token of the link, unescaped, with the new password, and says it worked with a link to sign in", async () => {
    const bodies = serveConfirm();
    renderWithProviders(<ResetPasswordForm />);

    await submit("palavra-passe-nova");

    expect(await screen.findByRole("status")).toHaveTextContent("A tua palavra-passe foi alterada.");
    expect(bodies).toEqual([{ email: "rita@exemplo.pt", token: "CfDJ8+a/b==", newPassword: "palavra-passe-nova" }]);
    expect(screen.getByRole("link", { name: "Entrar" })).toHaveAttribute("href", "/login");
    expect(screen.queryByLabelText("Nova palavra-passe")).not.toBeInTheDocument();
  });

  it("does not show the token or the email", () => {
    serveConfirm();
    renderWithProviders(<ResetPasswordForm />);

    expect(document.body.textContent).not.toContain("CfDJ8");
    expect(document.body.textContent).not.toContain("rita@exemplo.pt");
  });

  it.each([["1234567"], [""]])("refuses the password %j on the screen, with no request", async (password) => {
    const bodies = serveConfirm();
    renderWithProviders(<ResetPasswordForm />);

    await submit(password, password);

    expect(await screen.findByLabelText("Nova palavra-passe")).toHaveAccessibleDescription("A palavra-passe tem de ter pelo menos 8 caracteres.");
    expect(bodies).toEqual([]);
  });

  it("accepts exactly 8 characters", async () => {
    const bodies = serveConfirm();
    renderWithProviders(<ResetPasswordForm />);

    await submit("12345678");

    await screen.findByRole("status");
    expect(bodies[0].newPassword).toBe("12345678");
  });

  it("refuses two passwords that do not match, with no request", async () => {
    const bodies = serveConfirm();
    renderWithProviders(<ResetPasswordForm />);

    await submit("palavra-passe-nova", "palavra-passe-novb");

    expect(await screen.findByLabelText("Repetir a nova palavra-passe")).toHaveAccessibleDescription("As palavras-passe não coincidem.");
    expect(bodies).toEqual([]);
  });

  it("asks for the confirmation too", async () => {
    const bodies = serveConfirm();
    renderWithProviders(<ResetPasswordForm />);

    await submit("palavra-passe-nova", "");

    expect(await screen.findByLabelText("Repetir a nova palavra-passe")).toHaveAccessibleDescription("Repete a nova palavra-passe.");
    expect(bodies).toEqual([]);
  });

  it("shows the API's message for the password when it refuses it", async () => {
    serveConfirm(() => problem(400, "validation_failed", { newPassword: ["A palavra-passe tem de ter pelo menos 8 caracteres."] }));
    renderWithProviders(<ResetPasswordForm />);

    await submit("palavra-passe-nova");

    expect(await screen.findByLabelText("Nova palavra-passe")).toHaveAccessibleDescription("A palavra-passe tem de ter pelo menos 8 caracteres.");
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("says the link is no longer valid, with a link to ask for a new one, when it expired or was used", async () => {
    serveConfirm(() => problem(400, "invalid_or_expired_reset_link"));
    renderWithProviders(<ResetPasswordForm />);

    await submit("palavra-passe-nova");

    expect(await screen.findByRole("alert")).toHaveTextContent("Esta ligação já não é válida: expirou ou já foi usada. Pede uma nova.");
    expect(screen.getByRole("link", { name: "Pedir uma nova ligação" })).toHaveAttribute("href", "/forgot-password");
    expect(screen.queryByLabelText("Nova palavra-passe")).not.toBeInTheDocument();
  });

  it.each([["/reset-password"], ["/reset-password?email=rita%40exemplo.pt"], ["/reset-password?token=abc"]])(
    "a link without the email or the token (%s) says so at once, without asking the API",
    async (url) => {
      resetNavigation(url);
      const bodies = serveConfirm();
      renderWithProviders(<ResetPasswordForm />);

      expect(await screen.findByRole("alert")).toHaveTextContent("Esta ligação já não é válida");
      expect(screen.getByRole("link", { name: "Pedir uma nova ligação" })).toHaveAttribute("href", "/forgot-password");
      expect(bodies).toEqual([]);
    },
  );

  it("says to wait when there are too many requests, and keeps the form", async () => {
    serveConfirm(() => problem(429, "too_many_requests"));
    renderWithProviders(<ResetPasswordForm />);

    await submit("palavra-passe-nova");

    expect(await screen.findByRole("alert")).toHaveTextContent("Fizeste demasiados pedidos seguidos");
    await waitFor(() => expect(screen.getByLabelText("Nova palavra-passe")).toBeInTheDocument());
  });
});
