import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { currentPath, resetNavigation } from "@/test/navigation";
import { AccountStatus } from "@/features/auth/components/account-status";
import { PrivacySection } from "./privacy-section";

vi.mock("next/navigation", async () => import("@/test/navigation"));

function problem(status: number, code: string, extra: Record<string, unknown> = {}, headers: Record<string, string> = {}) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...extra }, { status, headers: { "Content-Type": "application/problem+json", ...headers } });
}

function serveAccount(profile = "Player") {
  let signedIn = true;
  server.use(
    http.get("/api/auth/me", () => (signedIn ? HttpResponse.json({ id: "1", name: "Rita Sousa", email: "rita@exemplo.test", profile }) : problem(401, "unauthorized"))),
    http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
  );
  return { signOut: () => (signedIn = false) };
}

/** Serves the deletion; returns the passwords the API received. */
function serveDelete(respond: () => Response) {
  const received: string[] = [];
  server.use(
    http.post("/api/me/delete-account", async ({ request }) => {
      received.push(((await request.json()) as { password: string }).password);
      return respond();
    }),
  );
  return received;
}

async function askToDelete(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Apagar a minha conta" }));
}

async function confirmWith(user: ReturnType<typeof userEvent.setup>, password: string) {
  await user.type(screen.getByLabelText("Palavra-passe"), password);
  await user.click(screen.getByRole("button", { name: "Sim, apagar a minha conta" }));
}

describe("PrivacySection", () => {
  const createObjectURL = vi.fn(() => "blob:os-meus-dados");
  const revokeObjectURL = vi.fn();
  const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);

  beforeEach(() => {
    resetNavigation("/account");
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    click.mockClear();
    createObjectURL.mockClear();
    revokeObjectURL.mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("downloads the file and says so", async () => {
    serveAccount();
    server.use(http.get("/api/me/data-export", () => HttpResponse.json({ account: { name: "Rita Sousa" } })));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await user.click(screen.getByRole("button", { name: "Exportar os meus dados" }));

    expect(await screen.findByText("Os teus dados foram descarregados (os-meus-dados.json).")).toHaveFocus();
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(click).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:os-meus-dados");
  });

  it("says how long to wait after too many downloads", async () => {
    serveAccount();
    server.use(http.get("/api/me/data-export", () => problem(429, "too_many_requests", {}, { "Retry-After": "30" })));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await user.click(screen.getByRole("button", { name: "Exportar os meus dados" }));

    expect(await screen.findByText(/Fizeste demasiados pedidos/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Exportar os meus dados" })).toBeDisabled();
    expect(click).not.toHaveBeenCalled();
  });

  it("explains what deleting does before asking for the password, and can be left", async () => {
    serveAccount();
    const received = serveDelete(() => new HttpResponse(null, { status: 204 }));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await askToDelete(user);

    const confirmation = screen.getByRole("group", { name: "Confirmar o apagamento da conta" });
    expect(confirmation).toHaveFocus();
    expect(confirmation).toHaveTextContent("As tuas reservas futuras são canceladas");
    await user.type(screen.getByLabelText("Palavra-passe"), "um-segredo");
    await user.click(screen.getByRole("button", { name: "Manter conta" }));

    expect(screen.queryByLabelText("Palavra-passe")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Apagar a minha conta" })).toHaveFocus();
    expect(received).toEqual([]);

    await askToDelete(user);
    expect(screen.getByLabelText("Palavra-passe")).toHaveValue("");
  });

  it("asks for the password before calling the API", async () => {
    serveAccount();
    const received = serveDelete(() => new HttpResponse(null, { status: 204 }));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await askToDelete(user);
    await user.click(screen.getByRole("button", { name: "Sim, apagar a minha conta" }));

    expect(await screen.findByText("Indica a palavra-passe.")).toBeInTheDocument();
    expect(received).toEqual([]);
  });

  it("deletes, confirms on screen and leaves the person signed out", async () => {
    const account = serveAccount();
    const received = serveDelete(() => {
      account.signOut();
      return new HttpResponse(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <AccountStatus />
        <PrivacySection />
      </>,
    );
    expect(await screen.findByText("Rita Sousa")).toBeInTheDocument();

    await askToDelete(user);
    await confirmWith(user, "a minha password");

    expect(await screen.findByRole("heading", { name: "Conta apagada" })).toHaveFocus();
    expect(received).toEqual(["a minha password"]);
    expect(screen.getByRole("link", { name: "Ir para a página inicial" })).toHaveAttribute("href", "/");
    await waitFor(() => expect(screen.queryByText("Rita Sousa")).not.toBeInTheDocument());
    expect(screen.getByRole("link", { name: "Entrar" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Exportar os meus dados" })).not.toBeInTheDocument();
  });

  it("shows a wrong password on the field, clears it and stays open", async () => {
    serveAccount();
    serveDelete(() => problem(400, "invalid_password", { errors: { password: ["A palavra-passe está incorreta."] } }));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await askToDelete(user);
    await confirmWith(user, "errada");

    expect(await screen.findByText("A palavra-passe está incorreta.")).toBeInTheDocument();
    expect(screen.getByLabelText("Palavra-passe")).toHaveValue("");
    expect(screen.queryByRole("heading", { name: "Conta apagada" })).not.toBeInTheDocument();
  });

  it("tells an owner with clubs to contact the administration", async () => {
    serveAccount("Owner");
    serveDelete(() => problem(422, "owner_has_clubs"));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await askToDelete(user);
    await confirmWith(user, "a-password");

    const notice = await screen.findByText(/não pode ser apagada aqui/);
    expect(notice).toHaveFocus();
    expect(notice).toHaveTextContent("Contacta a administração");
    expect(screen.getByRole("link", { name: /@/ })).toHaveAttribute("href", expect.stringMatching(/^mailto:/));
    expect(screen.queryByLabelText("Palavra-passe")).not.toBeInTheDocument();
  });

  it("does not offer deleting to the admin", async () => {
    serveAccount("Admin");
    renderWithProviders(<PrivacySection />);

    expect(await screen.findByText("A conta de administração não pode ser apagada.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Apagar a minha conta" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Exportar os meus dados" })).toBeInTheDocument();
  });

  it("goes to the login page when the session is gone", async () => {
    serveAccount();
    serveDelete(() => problem(401, "unauthorized"));
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await askToDelete(user);
    await confirmWith(user, "a-password");

    await waitFor(() => expect(currentPath()).toBe("/login?next=%2Faccount"));
  });
});
