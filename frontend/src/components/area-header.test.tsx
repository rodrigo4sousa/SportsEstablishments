import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

describe("the area menu (UX-005)", () => {
  const links = [
    { href: "/manage", label: "Os meus clubes" },
    { href: "/manage/statistics", label: "Estatísticas" },
  ];

  it("has the area's pages, the name and the way out in one «Conta» navigation", () => {
    renderWithProviders(<AreaHeader title="Área do dono" homeHref="/manage" accountName="Ana" links={links} />);

    const nav = screen.getByRole("navigation", { name: "Conta" });
    expect(nav).toHaveTextContent("Olá, Ana");
    expect(screen.getByRole("link", { name: "Os meus clubes" })).toHaveAttribute("href", "/manage");
    expect(screen.getByRole("link", { name: "Estatísticas" })).toHaveAttribute("href", "/manage/statistics");
    expect(screen.getByRole("button", { name: "Sair" })).toBeInTheDocument();
  });

  it("opens with the keyboard on a phone and Escape closes it, returning focus to the button", async () => {
    const user = userEvent.setup();
    renderWithProviders(<AreaHeader title="Área do dono" homeHref="/manage" accountName="Ana" links={links} />);

    const toggle = screen.getByRole("button", { name: "Conta" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    await user.tab();
    await user.tab();
    expect(toggle).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    await user.tab();
    expect(screen.getByRole("link", { name: "Os meus clubes" })).toHaveFocus();
    await user.keyboard("{Escape}");
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveFocus();
  });
});
