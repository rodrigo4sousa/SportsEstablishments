import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { PrivacySection } from "./privacy-section";

vi.mock("next/navigation", async () => import("@/test/navigation"));

function serveAccount(profile: string, preferredLanguage: string | null) {
  const bodies: unknown[] = [];
  server.use(
    http.get("/api/auth/me", () => HttpResponse.json({ id: "1", name: "Rita Sousa", email: "rita@exemplo.test", profile, preferredLanguage })),
    http.put("/api/me/preferred-language", async ({ request }) => {
      bodies.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return bodies;
}

describe("the language of the emails on the account page (T09)", () => {
  it("shows Portuguese for an account with no preference, and saves English", async () => {
    const bodies = serveAccount("Player", null);
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    const select = await screen.findByLabelText("Língua");
    expect(screen.getByRole("heading", { name: "Língua dos emails" })).toBeInTheDocument();
    expect(select).toHaveValue("pt-PT");

    await user.selectOptions(select, "en");
    await user.click(screen.getByRole("button", { name: "Guardar" }));

    expect(await screen.findByText("Língua dos emails guardada.")).toBeInTheDocument();
    expect(bodies).toEqual([{ preferredLanguage: "en" }]);
  });

  it("starts from the account's own choice, in English on an English page", async () => {
    serveAccount("Owner", "en");
    renderWithProviders(<PrivacySection />, { locale: "en" });

    expect(await screen.findByLabelText("Language")).toHaveValue("en");
    expect(screen.getByRole("heading", { name: "Email language" })).toBeInTheDocument();
  });

  it("is not offered to the admin, who gets no emails", async () => {
    serveAccount("Admin", null);
    renderWithProviders(<PrivacySection />);

    expect(await screen.findByText("A conta de administração não pode ser apagada.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Língua")).not.toBeInTheDocument();
  });

  it("says why when the API refuses, by code", async () => {
    serveAccount("Player", null);
    server.use(
      http.put("/api/me/preferred-language", () =>
        HttpResponse.json({ status: 403, code: "origin_not_allowed", title: "Origem", detail: "texto do servidor" }, { status: 403, headers: { "Content-Type": "application/problem+json" } }),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<PrivacySection />);

    await user.click(await screen.findByRole("button", { name: "Guardar" }));

    expect(await screen.findByText("O pedido foi recusado. Recarrega a página e tenta novamente.")).toBeInTheDocument();
    expect(screen.queryByText("texto do servidor")).not.toBeInTheDocument();
  });
});
