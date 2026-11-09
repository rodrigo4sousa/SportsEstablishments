import { render, screen, within } from "@testing-library/react";
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
    render(<Page />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { level: 1, name: title })).toBeInTheDocument();
    expect(screen.getByText(`Última atualização: ${formatDateKey(LEGAL_LAST_UPDATED)} · Versão ${LEGAL_VERSION}`)).toBeInTheDocument();
  });

  it.each(PAGES)("%s names every section after its heading", (_title, Page) => {
    render(<Page />);

    const sections = screen.getAllByRole("region");
    expect(sections.length).toBeGreaterThan(4);
    for (const section of sections) {
      expect(section).toHaveAccessibleName(within(section).getByRole("heading", { level: 2 }).textContent ?? "");
    }
  });

  it("the privacy policy names the controller and a working contact", () => {
    render(<PrivacyPage />);

    expect(screen.getByText(new RegExp(DATA_CONTROLLER_NAME))).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: DATA_PROTECTION_EMAIL })[0]).toHaveAttribute("href", `mailto:${DATA_PROTECTION_EMAIL}`);
  });

  it("the privacy policy lists the session cookies and the processors, and says what is still to confirm", () => {
    const { container } = render(<PrivacyPage />);
    const text = container.textContent ?? "";

    for (const name of ["access_token", "refresh_token", "Vercel", "Render", "Supabase", "Brevo", "GitHub", "CNPD", "2016/679"]) {
      expect(text).toContain(name);
    }
    expect(text).toMatch(/Brevo[\s\S]*Localização: a confirmar/);
  });

  it("the terms say it is a demo and which law applies", () => {
    const { container } = render(<TermsPage />);
    const text = container.textContent ?? "";

    expect(text).toContain("demonstração pessoal, sem fins comerciais");
    expect(text).toContain("lei portuguesa");
  });
});
