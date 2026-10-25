import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { adminOwnersQueryKey } from "../api";
import { DEFAULT_OWNERS } from "../admin-url";
import { OwnerDetails } from "./owner-details";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const OWNER_ID = "0199f7a0-0000-7000-8000-000000000001";

const owner = (patch: Record<string, unknown> = {}) => ({
  id: OWNER_ID,
  name: "Ana Dona",
  email: "ana@clube.test",
  registeredAt: "2026-07-14T09:30:00Z",
  deactivated: false,
  clubs: 2,
  pitches: 7,
  bookings: 1234,
  players: 80,
  futureBookings: 3,
  ...patch,
});

const change = (patch: Record<string, unknown> = {}) => ({
  id: "0199f7a0-0000-7000-8000-0000000000c1",
  action: "deactivated",
  reason: "Abuso da plataforma",
  adminId: "0199f7a0-0000-7000-8000-0000000000a1",
  adminName: "Administrador",
  at: "2026-10-20T09:15:00Z",
  ...patch,
});

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) }, { status, headers: { "Content-Type": "application/problem+json" } });
}

type State = { owner: ReturnType<typeof owner>; history: ReturnType<typeof change>[] };

/** An owner that changes as the API does: a deactivation or a reactivation updates the owner and adds a history entry. */
function serve(initial: Partial<State> = {}, options: { post?: (action: string, body: { reason: string }) => Response | null } = {}) {
  const state: State = { owner: owner(), history: [], ...initial };
  const posted: { action: string; body: { reason: string } }[] = [];
  const gets = { owner: 0, history: 0 };
  server.use(
    http.get(`/api/admin/owners/${OWNER_ID}`, () => {
      gets.owner += 1;
      return HttpResponse.json(state.owner);
    }),
    http.get(`/api/admin/owners/${OWNER_ID}/history`, () => {
      gets.history += 1;
      return HttpResponse.json(state.history);
    }),
    http.post(`/api/admin/owners/${OWNER_ID}/:action`, async ({ request, params }) => {
      const action = String(params.action);
      const body = (await request.json()) as { reason: string };
      posted.push({ action, body });
      const custom = options.post?.(action, body);
      if (custom) return custom;
      state.owner = { ...state.owner, deactivated: action === "deactivate" };
      state.history = [change({ id: `0199f7a0-0000-7000-8000-0000000000d${state.history.length}`, action: action === "deactivate" ? "deactivated" : "reactivated", reason: body.reason, at: "2026-10-25T14:00:00Z" }), ...state.history];
      return HttpResponse.json(state.owner);
    }),
  );
  return { state, posted, gets };
}

const form = (name: string) => screen.getByRole("form", { name });

describe("OwnerDetails", () => {
  beforeEach(() => {
    resetNavigation(`/admin/owners/${OWNER_ID}`);
  });

  describe("the owner", () => {
    it("shows the numbers, the status and the future bookings, with the date in Lisbon", async () => {
      serve();

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      expect(await screen.findByRole("heading", { name: "Ana Dona" })).toBeInTheDocument();
      expect(screen.getByText("Ativo")).toBeInTheDocument();
      const facts = Object.fromEntries(
        Array.from(document.querySelectorAll("dt")).map((term) => [term.textContent, (term.nextElementSibling?.textContent ?? "").replace(/\s/g, " ")]),
      );
      expect(facts).toEqual({
        Email: "ana@clube.test",
        Registo: "14/07/2026",
        Clubes: "2",
        "Campos ativos": "7",
        Reservas: "1234", // pt-PT does not group four digits
        Jogadores: "80",
        "Reservas futuras confirmadas": "3",
      });
      expect(screen.getByRole("link", { name: "← Donos" })).toHaveAttribute("href", "/admin");
    });

    it("marks a deactivated owner and offers to reactivate, not to deactivate", async () => {
      serve({ owner: owner({ deactivated: true }) });

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      expect(await screen.findByRole("button", { name: "Reativar dono" })).toBeInTheDocument();
      expect(screen.getByText("Desativado")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Desativar dono" })).not.toBeInTheDocument();
    });

    it("says an id that is not an owner's does not exist, with a way back", async () => {
      server.use(http.get(`/api/admin/owners/${OWNER_ID}`, () => problem(404, "owner_not_found")));

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      expect(await screen.findByText("Este dono já não existe.")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Voltar aos donos" })).toHaveAttribute("href", "/admin");
      expect(screen.queryByRole("button", { name: /dono$/ })).not.toBeInTheDocument();
    });

    it("shows a message and tries again on request when the owner cannot be loaded", async () => {
      let fail = true;
      server.use(
        http.get(`/api/admin/owners/${OWNER_ID}`, () => (fail ? problem(500, "internal_error") : HttpResponse.json(owner()))),
        http.get(`/api/admin/owners/${OWNER_ID}/history`, () => HttpResponse.json([])),
      );
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      const message = await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
      fail = false;
      await user.click(within(message.closest("div")!).getByRole("button", { name: "Tentar novamente" }));

      expect(await screen.findByRole("heading", { name: "Ana Dona" })).toBeInTheDocument();
    });

    it("sends the admin to sign in, coming back here, when the session is over", async () => {
      server.use(
        http.get(`/api/admin/owners/${OWNER_ID}`, () => problem(401, "unauthorized")),
        http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 401 })),
      );

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      await waitFor(() => expect(replace).toHaveBeenCalledWith(`/login?next=${encodeURIComponent(`/admin/owners/${OWNER_ID}`)}`, undefined));
    });
  });

  describe("history", () => {
    it("lists the changes newest first, each with the date in Lisbon, what happened, the reason and the admin", async () => {
      serve({
        history: [
          change({ id: "0199f7a0-0000-7000-8000-0000000000c2", action: "reactivated", reason: "Pedido resolvido", at: "2026-10-25T00:30:00Z", adminName: "Rita Admin" }),
          change(),
        ],
      });

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      const entries = within(await screen.findByRole("list", { name: "Histórico de estados" })).getAllByRole("listitem");
      expect(entries).toHaveLength(2);
      // 00:30 UTC on 25 October is 01:30 in Lisbon (still summer time); 09:15 UTC on the 20th is 10:15.
      expect(entries[0].textContent?.replace(/\s/g, " ")).toBe("Reativado25/10/2026 01:30Pedido resolvidoPor Rita Admin");
      expect(entries[1].textContent?.replace(/\s/g, " ")).toBe("Desativado20/10/2026 10:15Abuso da plataformaPor Administrador");
    });

    it("says the owner was never deactivated when there is no history", async () => {
      serve();

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      expect(await screen.findByText("Este dono nunca foi desativado.")).toBeInTheDocument();
    });

    it("shows a message when the history cannot be loaded, and the rest of the page still works", async () => {
      serve();
      server.use(http.get(`/api/admin/owners/${OWNER_ID}/history`, () => problem(500, "internal_error")));

      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      expect(await screen.findByRole("heading", { name: "Ana Dona" })).toBeInTheDocument();
      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Desativar dono" })).toBeInTheDocument();
    });
  });

  describe("deactivating", () => {
    it.each([
      [0, "Não há reservas futuras nos clubes deste dono."],
      [1, "1 reserva futura continua confirmada: não é cancelada."],
      [12, "12 reservas futuras continuam confirmadas: não são canceladas."],
    ])("says before the admin confirms how many future bookings stay confirmed (%i)", async (count, text) => {
      const { posted } = serve({ owner: owner({ futureBookings: count }) });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);

      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));

      const open = form("Desativar dono");
      expect(within(open).getByText(text)).toBeInTheDocument();
      expect(within(open).getByText(/deixa de poder entrar/)).toBeInTheDocument();
      expect(posted).toEqual([]);
    });

    it.each([
      ["nothing", "", "Indica o motivo."],
      ["only spaces", "    ", "Indica o motivo."],
      ["501 characters", "x".repeat(501), "O motivo pode ter no máximo 500 caracteres."],
    ])("needs a reason: %s is refused on the screen, with no request", async (_, reason, message) => {
      const { posted } = serve();
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));

      if (reason) {
        await user.click(within(form("Desativar dono")).getByLabelText("Motivo da desativação"));
        await user.paste(reason);
      }
      await user.click(screen.getByRole("button", { name: "Confirmar desativação" }));

      expect(await within(form("Desativar dono")).findByText(message)).toBeInTheDocument();
      expect(posted).toEqual([]);
    });

    it("sends the trimmed reason, then shows the owner deactivated, the new history entry, and reloads the list and the totals", async () => {
      const { posted, gets } = serve();
      const user = userEvent.setup();
      const { queryClient } = renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));
      queryClient.setQueryData(adminOwnersQueryKey(DEFAULT_OWNERS), { items: [], page: 1, pageSize: 20, totalCount: 0 });
      queryClient.setQueryData(["admin", "stats"], {});

      await user.type(screen.getByLabelText("Motivo da desativação"), "  Abuso da plataforma  ");
      await user.click(screen.getByRole("button", { name: "Confirmar desativação" }));

      expect(await screen.findByText("Dono desativado.")).toBeInTheDocument();
      expect(posted).toEqual([{ action: "deactivate", body: { reason: "Abuso da plataforma" } }]);
      // The badge next to the name (the history has its own «Desativado»).
      expect(within(screen.getByRole("heading", { name: "Ana Dona" }).parentElement!).getByText("Desativado")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Reativar dono" })).toBeInTheDocument();
      expect(screen.queryByRole("form")).not.toBeInTheDocument();
      const entries = await within(await screen.findByRole("list", { name: "Histórico de estados" })).findAllByRole("listitem");
      expect(entries).toHaveLength(1);
      expect(entries[0]).toHaveTextContent("Abuso da plataforma");
      expect(entries[0]).toHaveTextContent("25/10/2026 14:00");
      expect(gets.owner).toBeGreaterThanOrEqual(2);
      expect(gets.history).toBeGreaterThanOrEqual(2);
      expect(queryClient.getQueryState(adminOwnersQueryKey(DEFAULT_OWNERS))?.isInvalidated).toBe(true);
      expect(queryClient.getQueryState(["admin", "stats"])?.isInvalidated).toBe(true);
    });

    it("goes back without changing anything", async () => {
      const { posted } = serve();
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));

      await user.click(screen.getByRole("button", { name: "Voltar" }));

      expect(screen.queryByRole("form")).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Desativar dono" })).toBeInTheDocument();
      expect(posted).toEqual([]);
    });

    it("shows the API's message for the reason when it refuses it", async () => {
      serve({}, { post: () => problem(400, "validation_failed", { reason: ["Indica o motivo."] }) });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));

      await user.type(screen.getByLabelText("Motivo da desativação"), "Motivo");
      await user.click(screen.getByRole("button", { name: "Confirmar desativação" }));

      expect(await within(form("Desativar dono")).findByText("Indica o motivo.")).toBeInTheDocument();
    });

    it("says the owner is already deactivated (422), closes the form and shows the owner as the API has them", async () => {
      const { state } = serve({}, { post: () => problem(422, "owner_already_deactivated") });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));
      // Another admin got there first.
      state.owner = { ...state.owner, deactivated: true };
      state.history = [change()];

      await user.type(screen.getByLabelText("Motivo da desativação"), "Também eu");
      await user.click(screen.getByRole("button", { name: "Confirmar desativação" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("Este dono já está desativado.");
      expect(await screen.findByRole("button", { name: "Reativar dono" })).toBeInTheDocument();
      expect(await screen.findByRole("list", { name: "Histórico de estados" })).toBeInTheDocument();
    });

    it("says the owner no longer exists (404 on the change)", async () => {
      serve({}, { post: () => problem(404, "owner_not_found") });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));
      await user.type(screen.getByLabelText("Motivo da desativação"), "Motivo");
      // The owner is gone by the time the page asks again.
      server.use(http.get(`/api/admin/owners/${OWNER_ID}`, () => problem(404, "owner_not_found")));

      await user.click(screen.getByRole("button", { name: "Confirmar desativação" }));

      expect(await screen.findByText("Este dono já não existe.")).toBeInTheDocument();
    });

    it("shows a toast for any other failure and keeps the form with what was typed", async () => {
      serve({}, { post: () => problem(500, "internal_error") });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Desativar dono" }));

      await user.type(screen.getByLabelText("Motivo da desativação"), "Motivo");
      await user.click(screen.getByRole("button", { name: "Confirmar desativação" }));

      expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
      expect(screen.getByLabelText("Motivo da desativação")).toHaveValue("Motivo");
    });
  });

  describe("reactivating", () => {
    it("asks for a reason too, and says nothing about future bookings", async () => {
      const { posted } = serve({ owner: owner({ deactivated: true }), history: [change()] });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Reativar dono" }));

      expect(within(form("Reativar dono")).queryByText(/reservas? futuras?/)).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Confirmar reativação" }));

      expect(await within(form("Reativar dono")).findByText("Indica o motivo.")).toBeInTheDocument();
      expect(posted).toEqual([]);
    });

    it("sends the reason, then shows the owner active with both entries of the history", async () => {
      const { posted } = serve({ owner: owner({ deactivated: true }), history: [change()] });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Reativar dono" }));

      await user.type(screen.getByLabelText("Motivo da reativação"), "Pedido resolvido");
      await user.click(screen.getByRole("button", { name: "Confirmar reativação" }));

      expect(await screen.findByText("Dono reativado.")).toBeInTheDocument();
      expect(posted).toEqual([{ action: "reactivate", body: { reason: "Pedido resolvido" } }]);
      expect(screen.getByText("Ativo")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Desativar dono" })).toBeInTheDocument();
      const entries = await waitFor(() => {
        const found = within(screen.getByRole("list", { name: "Histórico de estados" })).getAllByRole("listitem");
        expect(found).toHaveLength(2);
        return found;
      });
      expect(entries[0]).toHaveTextContent("Reativado");
      expect(entries[1]).toHaveTextContent("Desativado");
    });

    it("says the owner is not deactivated (422) and shows them as the API has them", async () => {
      const { state } = serve({ owner: owner({ deactivated: true }), history: [change()] }, { post: () => problem(422, "owner_not_deactivated") });
      const user = userEvent.setup();
      renderWithProviders(<OwnerDetails ownerId={OWNER_ID} />);
      await user.click(await screen.findByRole("button", { name: "Reativar dono" }));
      state.owner = { ...state.owner, deactivated: false };

      await user.type(screen.getByLabelText("Motivo da reativação"), "Motivo");
      await user.click(screen.getByRole("button", { name: "Confirmar reativação" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("Este dono não está desativado.");
      expect(await screen.findByRole("button", { name: "Desativar dono" })).toBeInTheDocument();
    });
  });
});
