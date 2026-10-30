import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test/render";
import { AreaHeader } from "./area-header";
import { PAGE_WIDTH } from "./page-width";
import { PageShell } from "./page-shell";

vi.mock("next/navigation", async () => import("@/test/navigation"));

describe("page widths (UX-028)", () => {
  it("the header and the page of the same area use the same width", () => {
    const { container } = renderWithProviders(
      <>
        <AreaHeader title="Administração" homeHref="/admin" accountName="Ana" width="wide" />
        <PageShell title="Administração" width="wide">
          corpo
        </PageShell>
      </>,
    );

    const header = screen.getByRole("banner").firstElementChild;
    const page = container.querySelector("section");
    expect(header?.className).toContain(PAGE_WIDTH.wide);
    expect(page?.className).toContain(PAGE_WIDTH.wide);
  });

  it("is narrow by default for both, as the owner and player areas are", () => {
    const { container } = renderWithProviders(
      <>
        <AreaHeader title="Área do dono" homeHref="/manage" accountName="Ana" />
        <PageShell title="Os teus clubes">corpo</PageShell>
      </>,
    );

    expect(screen.getByRole("banner").firstElementChild?.className).toContain(PAGE_WIDTH.narrow);
    expect(container.querySelector("section")?.className).toContain(PAGE_WIDTH.narrow);
  });
});
