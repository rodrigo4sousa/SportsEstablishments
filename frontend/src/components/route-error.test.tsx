import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { RouteError } from "./route-error";

describe("RouteError", () => {
  it("shows a pt-PT message and retries when the button is clicked", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const retry = vi.fn();

    render(<RouteError error={new Error("boom")} retry={retry} />);

    // The page it replaces had focus, so the error takes it (UX-002); it is a heading, not also a live region.
    const heading = screen.getByRole("heading", { name: "Ocorreu um erro inesperado." });
    expect(heading).toHaveFocus();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));
    expect(retry).toHaveBeenCalledOnce();
  });

  it("has a way out: a link back to the start", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});

    render(<RouteError error={new Error("boom")} retry={() => {}} />);

    expect(screen.getByRole("link", { name: "Voltar ao início" })).toHaveAttribute("href", "/");
  });
});
