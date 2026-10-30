import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicClub } from "@/features/catalog/types";
import { resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

const club: PublicClub = {
  name: "Arena Futsal Boavista",
  slug: "arena-futsal-boavista",
  address: "Avenida da Boavista 2450",
  locality: "Porto",
  phone: "226100102",
  contactEmail: "geral@exemplo.test",
  pitches: [{ id: "0199f7a0-0000-7000-8000-000000000001", name: "Futsal A", sport: "futsal", pricePerSlot: 38, hasPriceBands: true }],
} as PublicClub;

vi.mock("@/lib/server/public-club", () => ({ getPublicClub: vi.fn(), loadPublicClub: async () => club }));
vi.mock("next/navigation", async () => import("@/test/navigation"));

const { default: Page } = await import("./page");

describe("the club page (UX-010)", () => {
  beforeEach(() => {
    resetNavigation("/clubs/arena-futsal-boavista");
    server.use(
      http.get("/api/auth/me", () => new HttpResponse(null, { status: 401 })),
      http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 401 })),
      http.get("/api/clubs/:slug/pitches/:pitch/slots", ({ request }) =>
        HttpResponse.json({ date: new URL(request.url).searchParams.get("date"), slots: [] }),
      ),
    );
  });

  async function render() {
    renderWithProviders(await Page({ params: Promise.resolve({ slug: club.slug }), searchParams: Promise.resolve({}) }));
    await screen.findByRole("heading", { level: 2, name: "Horários" });
  }

  const before = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

  it("puts the slots first, then the pitches, then the contacts", async () => {
    await render();

    const headings = ["Horários", "Campos", "Contactos"].map((name) => screen.getByRole("heading", { level: 2, name }));
    expect(before(headings[0], headings[1])).toBe(true);
    expect(before(headings[1], headings[2])).toBe(true);
  });

  it("puts the way back above the title, and says where the club is under it", async () => {
    await render();

    const back = screen.getByRole("link", { name: "← Pesquisar clubes" });
    const title = screen.getByRole("heading", { level: 1, name: "Arena Futsal Boavista" });
    expect(back).toHaveAttribute("href", "/");
    expect(before(back, title)).toBe(true);
    expect(before(title, screen.getByText("Avenida da Boavista 2450, Porto", { selector: "p" }))).toBe(true);
  });

  it("says how to book, in one line, before the slots", async () => {
    await render();

    expect(screen.getByText("Escolhe o campo e o dia, depois um horário livre, e confirma a reserva.")).toBeInTheDocument();
  });

  it("does not quote one price for a pitch with price bands in the pitch choice, since each slot has its own (UX-026)", async () => {
    await render();

    expect(screen.getByRole("option", { name: "Futsal A · Futsal" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /38,00/ })).not.toBeInTheDocument();
  });
});
