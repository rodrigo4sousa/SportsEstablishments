import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { currentPath, replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { ClubSearchItem } from "../types";
import { ClubSearch } from "./club-search";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const club = (name: string, patch: Partial<ClubSearchItem> = {}): ClubSearchItem => ({
  slug: name.toLowerCase().replaceAll(" ", "-"),
  name,
  locality: "Porto",
  sports: ["padel"],
  hasFreeSlots: true,
  ...patch,
});

function page(items: ClubSearchItem[], extra: { page?: number; totalCount?: number } = {}) {
  return HttpResponse.json({ items, page: extra.page ?? 1, pageSize: 20, totalCount: extra.totalCount ?? items.length });
}

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

/** Every search the API receives, as its query string. */
function serveSearch(respond: (query: URLSearchParams) => Response = () => page([club("Clube da Foz")])) {
  const asked: URLSearchParams[] = [];
  server.use(
    http.get("/api/localities", () => HttpResponse.json(["Évora", "Porto"])),
    http.get("/api/clubs", ({ request }) => {
      const query = new URL(request.url).searchParams;
      asked.push(query);
      return respond(query);
    }),
  );
  return asked;
}

const sportField = () => screen.getByLabelText("Desporto") as HTMLSelectElement;
const localityField = () => screen.getByLabelText("Localidade") as HTMLSelectElement;
const dateField = () => screen.getByLabelText("Data") as HTMLInputElement;

describe("ClubSearch", () => {
  beforeEach(() => {
    // 10:00 UTC on Friday 1 May 2026 is 11:00 in Lisbon.
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-01T10:00:00Z"));
    resetNavigation("/");
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("searches without a session and shows name, locality, sports and the free-slot indicator", async () => {
    // No handler for /api/auth/me: the test fails if the search asked who is signed in.
    serveSearch(() =>
      page([
        club("Clube da Foz", { locality: "Porto", sports: ["padel", "tennis"], hasFreeSlots: true }),
        club("Évora Padel", { locality: "Évora", sports: ["padel"], hasFreeSlots: false }),
      ]),
    );

    renderWithProviders(<ClubSearch />);

    const list = await screen.findByRole("list", { name: "Clubes" });
    const [first, second] = within(list).getAllByRole("listitem");
    expect(within(first).getByRole("link", { name: "Clube da Foz" })).toHaveAttribute("href", "/clubs/clube-da-foz");
    expect(first).toHaveTextContent("Porto · Padel, Ténis");
    expect(first).toHaveTextContent("Há horários livres");
    expect(second).toHaveTextContent("Évora · Padel");
    expect(second).toHaveTextContent("Sem horários livres");
    expect(screen.getByText(/2 clubes · sexta-feira, 1 de maio/)).toBeInTheDocument();
  });

  it("starts with every sport and locality, today as the date, limited to today..+30 days", async () => {
    const asked = serveSearch();

    renderWithProviders(<ClubSearch />);

    await screen.findByRole("list", { name: "Clubes" });
    expect(sportField()).toHaveValue("");
    expect(within(sportField()).getAllByRole("option").map((option) => option.textContent)).toEqual([
      "Todos os desportos",
      "Futsal",
      "Futebol 7",
      "Padel",
      "Ténis",
    ]);
    await waitFor(() =>
      expect(within(localityField()).getAllByRole("option").map((option) => option.textContent)).toEqual(["Todas as localidades", "Évora", "Porto"]),
    );
    expect(dateField()).toHaveValue("2026-05-01");
    expect(dateField()).toHaveAttribute("min", "2026-05-01");
    expect(dateField()).toHaveAttribute("max", "2026-05-31");
    expect(asked).toHaveLength(1);
    expect([...asked[0].keys()]).toEqual([]); // no filters: the API means today and every sport
  });

  it("uses the Lisbon date as today, not the UTC date", async () => {
    // 23:30 UTC on Sunday 3 May is already Monday 4 May in Lisbon.
    vi.setSystemTime(new Date("2026-05-03T23:30:00Z"));
    serveSearch();

    renderWithProviders(<ClubSearch />);

    await screen.findByRole("list", { name: "Clubes" });
    expect(dateField()).toHaveValue("2026-05-04");
    expect(dateField()).toHaveAttribute("min", "2026-05-04");
  });

  it("searches again when the sport, the locality or the date change, and keeps the search in the URL", async () => {
    const asked = serveSearch();
    renderWithProviders(<ClubSearch />);
    await screen.findByRole("list", { name: "Clubes" });
    await screen.findByRole("option", { name: "Évora" });

    await userEvent.selectOptions(sportField(), "padel");
    await waitFor(() => expect(asked.at(-1)?.get("sport")).toBe("padel"));
    await userEvent.selectOptions(localityField(), "Évora");
    await waitFor(() => expect(asked.at(-1)?.get("locality")).toBe("Évora"));
    fireEvent.change(dateField(), { target: { value: "2026-05-04" } });
    await waitFor(() => expect(asked.at(-1)?.get("date")).toBe("2026-05-04"));

    expect(Object.fromEntries(asked.at(-1)!)).toEqual({ sport: "padel", locality: "Évora", date: "2026-05-04" });
    expect(currentPath()).toBe("/?sport=padel&locality=%C3%89vora&date=2026-05-04");
    expect(sportField()).toHaveValue("padel");
    expect(localityField()).toHaveValue("Évora");
    expect(dateField()).toHaveValue("2026-05-04");
  });

  it("starts from the search in the URL", async () => {
    resetNavigation("/?sport=tennis&locality=Porto&date=2026-05-10&page=2");
    const asked = serveSearch(() => page([club("Clube da Foz")], { page: 2, totalCount: 21 }));

    renderWithProviders(<ClubSearch />);

    await screen.findByRole("list", { name: "Clubes" });
    expect(Object.fromEntries(asked[0])).toEqual({ sport: "tennis", locality: "Porto", date: "2026-05-10", page: "2" });
    expect(sportField()).toHaveValue("tennis");
    expect(localityField()).toHaveValue("Porto");
    expect(dateField()).toHaveValue("2026-05-10");
  });

  it("opens a club on the date that was searched", async () => {
    resetNavigation("/?date=2026-05-04");
    serveSearch();

    renderWithProviders(<ClubSearch />);

    expect(await screen.findByRole("link", { name: "Clube da Foz" })).toHaveAttribute("href", "/clubs/clube-da-foz?date=2026-05-04");
  });

  it("says so clearly when nothing is found", async () => {
    serveSearch(() => page([]));

    renderWithProviders(<ClubSearch />);

    expect(await screen.findByText(/Não encontrámos clubes com estes filtros/)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Clubes" })).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: "Páginas" })).not.toBeInTheDocument();
  });

  describe("pages", () => {
    const manyPages = (query: URLSearchParams) => {
      const current = Number(query.get("page") ?? "1");
      return page([club(`Clube ${current}`)], { page: current, totalCount: 45 });
    };

    it("shows the page, goes forward and back, and stops at the ends", async () => {
      const asked = serveSearch(manyPages);
      renderWithProviders(<ClubSearch />);
      await screen.findByRole("link", { name: "Clube 1" });
      const pagination = screen.getByRole("navigation", { name: "Páginas" });
      expect(within(pagination).getByText("Página 1 de 3")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Anterior" })).toBeDisabled();

      await userEvent.click(screen.getByRole("button", { name: "Seguinte" }));
      await screen.findByRole("link", { name: "Clube 2" });
      expect(asked.at(-1)?.get("page")).toBe("2");
      expect(currentPath()).toBe("/?page=2");
      expect(screen.getByText("Página 2 de 3")).toBeInTheDocument();

      await userEvent.click(screen.getByRole("button", { name: "Seguinte" }));
      await screen.findByRole("link", { name: "Clube 3" });
      expect(screen.getByRole("button", { name: "Seguinte" })).toBeDisabled();

      await userEvent.click(screen.getByRole("button", { name: "Anterior" }));
      await screen.findByRole("link", { name: "Clube 2" });
      expect(currentPath()).toBe("/?page=2");
    });

    it("goes back to the first page when a filter changes", async () => {
      resetNavigation("/?page=3");
      const asked = serveSearch(manyPages);
      renderWithProviders(<ClubSearch />);
      await screen.findByRole("link", { name: "Clube 3" });

      await userEvent.selectOptions(sportField(), "tennis");

      await waitFor(() => expect(currentPath()).toBe("/?sport=tennis"));
      await waitFor(() => expect(asked.at(-1)?.get("page")).toBeNull());
    });

    it("offers the first page when the page asked for is past the end", async () => {
      resetNavigation("/?page=9");
      serveSearch((query) => (query.get("page") === "9" ? page([], { page: 9, totalCount: 45 }) : manyPages(query)));
      renderWithProviders(<ClubSearch />);

      expect(await screen.findByText("Esta página não tem resultados.")).toBeInTheDocument();
      await userEvent.click(screen.getByRole("button", { name: "Voltar à primeira página" }));

      expect(await screen.findByRole("link", { name: "Clube 1" })).toBeInTheDocument();
      expect(currentPath()).toBe("/");
    });
  });

  it("does not ask the API for a date outside today..+30 days, and says why", async () => {
    resetNavigation("/?date=2026-07-01");
    // No /api/clubs handler: the test fails if a request is made.
    server.use(http.get("/api/localities", () => HttpResponse.json([])));

    renderWithProviders(<ClubSearch />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Escolhe uma data de hoje até 30 dias à frente.");
    expect(dateField()).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("list", { name: "Clubes" })).not.toBeInTheDocument();
  });

  it("drops a sport that is not in the list instead of searching for it", async () => {
    resetNavigation("/?sport=golfe");
    const asked = serveSearch();

    renderWithProviders(<ClubSearch />);

    await screen.findByRole("list", { name: "Clubes" });
    expect(asked[0].get("sport")).toBeNull();
    expect(sportField()).toHaveValue("");
  });

  it("keeps a locality from the link that the list does not have as the chosen one", async () => {
    resetNavigation("/?locality=Faro");
    serveSearch();

    renderWithProviders(<ClubSearch />);

    await screen.findByRole("list", { name: "Clubes" });
    await waitFor(() => expect(within(localityField()).getAllByRole("option").map((option) => option.textContent)).toEqual(["Todas as localidades", "Faro", "Évora", "Porto"]));
    expect(localityField()).toHaveValue("Faro");
  });

  it("shows a message and retries when the search fails", async () => {
    let fails = true;
    serveSearch(() => (fails ? problem(500, "internal_error") : page([club("Clube da Foz")])));
    renderWithProviders(<ClubSearch />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado.");
    fails = false;
    await userEvent.click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("link", { name: "Clube da Foz" })).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
  });
});
