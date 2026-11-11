import { IntlProvider } from "@/test/intl";
import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { pageTitle, useDocumentTitle } from "./document-title";

describe("the title of a page that names what it shows", () => {
  beforeEach(() => {
    document.title = "Calendário · Reservas de Campos";
  });

  it("adds the name to the tab title once it is known, keeping the site suffix", () => {
    renderHook(() => useDocumentTitle("Calendário", "Padel Foz Clube"), { wrapper: IntlProvider });

    expect(document.title).toBe("Calendário · Padel Foz Clube · Reservas de Campos");
    expect(pageTitle("Reservas de Campos", "Estatísticas", "Clube")).toBe("Estatísticas · Clube · Reservas de Campos");
  });

  it("leaves the title alone while the name is not known", () => {
    renderHook(() => useDocumentTitle("Calendário", undefined), { wrapper: IntlProvider });

    expect(document.title).toBe("Calendário · Reservas de Campos");
  });
});
