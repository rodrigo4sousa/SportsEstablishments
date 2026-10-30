import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ClubList } from "./club-list";

function club(n: number) {
  return {
    id: `0199f7a0-0000-7000-8000-${String(n).padStart(12, "0")}`,
    slug: `clube-${n}`,
    name: `Clube ${n}`,
    address: `Rua ${n}`,
    locality: n % 2 ? "Porto" : "Lisboa",
    phone: "912345678",
    contactEmail: "geral@clube.pt",
    cancellationWindowHours: 24,
    timeZone: "Europe/Lisbon",
  };
}

function page(
  items: ReturnType<typeof club>[],
  pageNumber: number,
  totalCount: number,
) {
  return HttpResponse.json({
    items,
    page: pageNumber,
    pageSize: 20,
    totalCount,
  });
}

describe("ClubList", () => {
  it("shows the empty state, inviting the owner to create the first club", async () => {
    server.use(http.get("/api/manage/clubs", () => page([], 1, 0)));

    renderWithProviders(<ClubList />);

    expect(
      await screen.findByRole("heading", {
        name: "Ainda não tens nenhum clube",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Criar o primeiro clube" }),
    ).toHaveAttribute("href", "/manage/clubs/new");
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });

  it("lists the owner's clubs, each linking to its page, with a way to add another", async () => {
    server.use(
      http.get("/api/manage/clubs", () => page([club(1), club(2)], 1, 2)),
    );

    renderWithProviders(<ClubList />);

    const first = await screen.findByRole("link", { name: "Editar Clube 1" });
    expect(first).toHaveAttribute(
      "href",
      "/manage/clubs/0199f7a0-0000-7000-8000-000000000001",
    );
    expect(first.closest("li")).toHaveTextContent("Porto · Rua 1");
    expect(
      screen.getByRole("link", { name: "Editar Clube 2" }),
    ).toHaveAttribute(
      "href",
      "/manage/clubs/0199f7a0-0000-7000-8000-000000000002",
    );
    expect(screen.getByRole("link", { name: "Novo clube" })).toHaveAttribute(
      "href",
      "/manage/clubs/new",
    );
    // US14: the statistics of all the owner's clubs.
    expect(screen.getByRole("link", { name: "Estatísticas" })).toHaveAttribute(
      "href",
      "/manage/statistics",
    );
    expect(
      screen.queryByRole("heading", { name: "Ainda não tens nenhum clube" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("navigation", { name: "Páginas" }),
    ).not.toBeInTheDocument();
  });

  it("puts the calendar, the statistics and the editing of each club one tap away (UX-013)", async () => {
    server.use(
      http.get("/api/manage/clubs", () => page([club(1), club(2)], 1, 2)),
    );

    renderWithProviders(<ClubList />);

    const id = "0199f7a0-0000-7000-8000-000000000002";
    expect(
      await screen.findByRole("link", { name: "Calendário de Clube 2" }),
    ).toHaveAttribute("href", `/manage/clubs/${id}/calendar`);
    expect(
      screen.getByRole("link", { name: "Estatísticas de Clube 2" }),
    ).toHaveAttribute("href", `/manage/clubs/${id}/statistics`);
    expect(
      screen.getByRole("link", { name: "Editar Clube 2" }),
    ).toHaveAttribute("href", `/manage/clubs/${id}`);
    // The visible word is the start of the accessible name (UX-016).
    expect(
      screen.getByRole("link", { name: "Calendário de Clube 2" }),
    ).toHaveTextContent("Calendário");
  });

  it("shows a skeleton with one status message while it loads", async () => {
    server.use(
      http.get("/api/manage/clubs", async () => {
        await new Promise((r) => setTimeout(r, 50));
        return page([club(1)], 1, 1);
      }),
    );

    renderWithProviders(<ClubList />);

    expect(screen.getByRole("status")).toHaveTextContent(
      "A carregar os teus clubes…",
    );
    await screen.findByRole("link", { name: "Editar Clube 1" });
  });

  it("asks for the first page of 20", async () => {
    let query = "";
    server.use(
      http.get("/api/manage/clubs", ({ request }) => {
        query = new URL(request.url).search;
        return page([club(1)], 1, 1);
      }),
    );

    renderWithProviders(<ClubList />);

    await screen.findByRole("link", { name: "Editar Clube 1" });
    expect(query).toBe("?page=1&pageSize=20");
  });

  it("pages through the clubs", async () => {
    const asked: string[] = [];
    server.use(
      http.get("/api/manage/clubs", ({ request }) => {
        const requested = Number(new URL(request.url).searchParams.get("page"));
        asked.push(String(requested));
        return requested === 1
          ? page([club(1)], 1, 21)
          : page([club(21)], 2, 21);
      }),
    );
    renderWithProviders(<ClubList />);
    const user = userEvent.setup();

    await screen.findByRole("link", { name: "Editar Clube 1" });
    expect(screen.getByText("Página 1 de 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Anterior" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Seguinte" }));

    expect(
      await screen.findByRole("link", { name: "Editar Clube 21" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Página 2 de 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Seguinte" })).toBeDisabled();
    expect(asked).toEqual(["1", "2"]);
    await user.click(screen.getByRole("button", { name: "Anterior" }));
    await waitFor(() =>
      expect(screen.getByText("Página 1 de 2")).toBeInTheDocument(),
    );
  });

  it("shows a message and a way to try again when the list cannot be loaded", async () => {
    let calls = 0;
    server.use(
      http.get("/api/manage/clubs", () => {
        calls++;
        return calls === 1
          ? HttpResponse.json(
              { status: 500, code: "internal_error", traceId: "t" },
              { status: 500 },
            )
          : page([club(1)], 1, 1);
      }),
    );
    renderWithProviders(<ClubList />);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Ocorreu um erro inesperado. Tenta novamente mais tarde.",
    );
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(
      await screen.findByRole("link", { name: "Editar Clube 1" }),
    ).toBeInTheDocument();
  });
});
