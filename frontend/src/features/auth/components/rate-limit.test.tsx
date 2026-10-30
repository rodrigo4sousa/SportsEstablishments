import { act, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import type { ReactElement } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ForgotPasswordForm } from "./forgot-password-form";
import { LoginForm } from "./login-form";
import { RegisterForm } from "./register-form";
import { ResetPasswordForm } from "./reset-password-form";

vi.mock("next/navigation", async () => import("@/test/navigation"));

/** UX-018: after a 429 that says how long to wait, every form that can get one counts it down and keeps its button off. */
type Case = {
  name: string;
  path: string;
  button: string;
  render: () => ReactElement;
  fill: (user: ReturnType<typeof userEvent.setup>) => Promise<void>;
};

const CASES: Case[] = [
  {
    name: "sign in",
    path: "/api/auth/login",
    button: "Entrar",
    render: () => <LoginForm next={null} />,
    fill: async (user) => {
      await user.type(screen.getByLabelText(/^email$/i), "ana@exemplo.pt");
      await user.type(screen.getByLabelText(/palavra-passe/i), "palavra-passe");
    },
  },
  {
    name: "sign up",
    path: "/api/auth/register",
    button: "Criar conta",
    render: () => <RegisterForm />,
    fill: async (user) => {
      await user.type(screen.getByLabelText(/^nome$/i), "Ana Silva");
      await user.type(screen.getByLabelText(/^email$/i), "ana@exemplo.pt");
      await user.type(screen.getByLabelText(/palavra-passe/i), "palavra-passe");
    },
  },
  {
    name: "password recovery",
    path: "/api/auth/password-reset/request",
    button: "Enviar ligação",
    render: () => <ForgotPasswordForm />,
    fill: async (user) => {
      await user.type(screen.getByLabelText(/^email$/i), "ana@exemplo.pt");
    },
  },
  {
    name: "new password",
    path: "/api/auth/password-reset/confirm",
    button: "Definir palavra-passe",
    render: () => <ResetPasswordForm />,
    fill: async (user) => {
      await user.type(screen.getByLabelText("Nova palavra-passe"), "palavra-passe");
      await user.type(screen.getByLabelText("Repetir a nova palavra-passe"), "palavra-passe");
    },
  },
];

function tooManyRequests(path: string, retryAfter: string | null) {
  server.use(
    http.post(path, () =>
      HttpResponse.json(
        { status: 429, title: "Erro", code: "too_many_requests", traceId: "t" },
        { status: 429, headers: { "Content-Type": "application/problem+json", ...(retryAfter ? { "Retry-After": retryAfter } : {}) } },
      ),
    ),
  );
}

describe.each(CASES)("the $name form after a 429", (form) => {
  beforeEach(() => {
    resetNavigation("/reset-password?email=ana%40exemplo.pt&token=abc");
    // Only the clock and the interval are fake: the mock server and user-event keep their real timers.
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval", "Date"] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  async function submitAndWait(retryAfter: string | null) {
    tooManyRequests(form.path, retryAfter);
    renderWithProviders(form.render());
    const user = userEvent.setup();
    await form.fill(user);
    await user.click(screen.getByRole("button", { name: form.button }));
  }

  it("says how long to wait, takes focus, and keeps the button off while it counts down", async () => {
    await submitAndWait("3");

    const timer = await screen.findByRole("timer");
    expect(timer).toHaveTextContent("3 segundos");
    const message = timer.closest("p");
    expect(message).toHaveTextContent("Fizeste demasiados pedidos seguidos. Podes tentar de novo dentro de 3 segundos.");
    expect(message).toHaveFocus();
    // It has focus, so it is not also a live region (UX-002).
    expect(message).not.toHaveAttribute("role");
    expect(screen.getByRole("button", { name: form.button })).toBeDisabled();

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(screen.getByRole("timer")).toHaveTextContent("2 segundos");
    expect(screen.getByRole("button", { name: form.button })).toBeDisabled();
  });

  it("says «1 segundo» in the singular, and then that the user can try again, with the button back", async () => {
    await submitAndWait("1");
    expect(await screen.findByRole("timer")).toHaveTextContent("1 segundo");

    act(() => {
      vi.advanceTimersByTime(1500);
    });

    expect(screen.queryByRole("timer")).not.toBeInTheDocument();
    expect(screen.getAllByText("Já podes tentar de novo.").length).toBeGreaterThan(0);
    expect(screen.getByRole("status")).toHaveTextContent("Já podes tentar de novo.");
    expect(screen.getByRole("button", { name: form.button })).toBeEnabled();
  });

  it("keeps the general message when the API sends no Retry-After", async () => {
    await submitAndWait(null);

    expect(await screen.findByText("Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.")).toBeInTheDocument();
    expect(screen.queryByRole("timer")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: form.button })).toBeEnabled();
  });
});
