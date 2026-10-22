import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { RouteError } from "./route-error";

describe("RouteError", () => {
  it("shows a pt-PT message and retries when the button is clicked", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const retry = vi.fn();

    render(<RouteError error={new Error("boom")} retry={retry} />);

    expect(screen.getByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado.");
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));
    expect(retry).toHaveBeenCalledOnce();
  });
});
