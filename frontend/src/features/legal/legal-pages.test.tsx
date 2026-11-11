import { renderWithIntl } from "@/test/intl";
import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import PrivacyPage from "@/app/(legal)/privacy/page";
import TermsPage from "@/app/(legal)/terms/page";
import { formatDateKey } from "@/lib/datetime";
import { DATA_CONTROLLER_NAME, DATA_PROTECTION_EMAIL, LEGAL_LAST_UPDATED, LEGAL_VERSION } from "./legal-info";

const PAGES = [
  ["Termos e Condições", TermsPage],
  ["Política de Privacidade", PrivacyPage],
] as const;

describe("legal pages", () => {
  it.each(PAGES)("%s has one h1, the date and the version from the one module", (title, Page) => {
    renderWithIntl(<Page />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { level: 1, name: title })).toBeInTheDocument();
    expect(screen.getByText(`Última atualização: ${formatDateKey(LEGAL_LAST_UPDATED)} · Versão ${LEGAL_VERSION}`)).toBeInTheDocument();
  });

  it.each(PAGES)("%s names every section after its heading", (_title, Page) => {
    renderWithIntl(<Page />);

    const sections = screen.getAllByRole("region");
    expect(sections.length).toBeGreaterThan(4);
    for (const section of sections) {
      expect(section).toHaveAccessibleName(within(section).getByRole("heading", { level: 2 }).textContent ?? "");
    }
  });

  it("the privacy policy names the controller and a working contact", () => {
    renderWithIntl(<PrivacyPage />);

    expect(screen.getByText(new RegExp(DATA_CONTROLLER_NAME))).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: DATA_PROTECTION_EMAIL })[0]).toHaveAttribute("href", `mailto:${DATA_PROTECTION_EMAIL}`);
  });

  it("the privacy policy lists the session cookies, the processors and where the data is", () => {
    const { container } = renderWithIntl(<PrivacyPage />);
    const text = container.textContent ?? "";

    for (const name of ["access_token", "refresh_token", "Vercel", "Render", "Supabase", "Brevo", "GitHub", "CNPD", "2016/679"]) {
      expect(text).toContain(name);
    }
    for (const fact of ["na Irlanda", "A API corre em Frankfurt", "Brevo, em França", "Washington DC", "Decisão 2021/914", "EU-U.S. Data Privacy Framework"]) {
      expect(text).toContain(fact);
    }
    // Nothing is left to confirm: the log retention is the free plans' (7 days on Render, 1 hour on Vercel).
    expect(text).not.toContain("a confirmar");
    expect(text).toContain("7 dias no Render");
    expect(text).toContain("durante 1 hora");
    expect(text).toContain("não escreve palavras-passe, tokens nem endereços de email");
  });

  it("the terms say it is a demo and which law applies", () => {
    const { container } = renderWithIntl(<TermsPage />);
    const text = container.textContent ?? "";

    expect(text).toContain("demonstração pessoal, sem fins comerciais");
    expect(text).toContain("lei portuguesa");
  });
});
