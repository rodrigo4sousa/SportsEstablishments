import { renderWithIntl } from "@/test/intl";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Main, MAIN_ID } from "./main";
import { SkipLink } from "./skip-link";

describe("the skip link (UX-014)", () => {
  it("points at the main content, in pt-PT, and is hidden until it has focus", () => {
    renderWithIntl(<SkipLink />);

    const link = screen.getByRole("link", { name: "Saltar para o conteúdo" });
    expect(link).toHaveAttribute("href", `#${MAIN_ID}`);
    expect(link.className).toContain("sr-only");
    expect(link.className).toContain("focus:not-sr-only");
  });

  it("has a target: the page's main, which can take focus when the link is used", () => {
    renderWithIntl(<Main>conteúdo</Main>);

    const main = screen.getByRole("main");
    expect(main).toHaveAttribute("id", MAIN_ID);
    expect(main).toHaveAttribute("tabindex", "-1");
  });
});
