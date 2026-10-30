import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { PublicHeader } from "./public-header";

vi.mock("next/navigation", async () => import("@/test/navigation"));

describe("the public header", () => {
  it("lets the brand and the sign-in links wrap onto two lines at 320 px, so the page never scrolls sideways (WCAG 1.4.10)", async () => {
    server.use(http.get("/api/auth/me", () => HttpResponse.json({ status: 401, code: "unauthorized", traceId: "t" }, { status: 401 })));

    renderWithProviders(<PublicHeader />);

    expect(screen.getByRole("banner").firstElementChild).toHaveClass("flex-wrap");
    expect(await screen.findByRole("link", { name: "Entrar" })).toBeInTheDocument();
  });
});
