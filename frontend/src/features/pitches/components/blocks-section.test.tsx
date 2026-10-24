import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { Block } from "../api";
import { BlocksSection } from "./blocks-section";
import { SlotPreview } from "./slot-preview";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const pitchId = "0199f7a0-0000-7000-8000-0000000000d1";
const url = `/api/manage/clubs/${clubId}/pitches/${pitchId}/blocks`;
const onGone = vi.fn();

function block(n: number, overrides: Partial<Block> = {}): Block {
  return {
    id: `0199f7a0-0000-7000-8000-${String(n).padStart(12, "0")}`,
    pitchId,
    startsAt: "2099-05-04T08:00:00Z",
    endsAt: "2099-05-04T10:00:00Z",
    reason: null,
    ...overrides,
  };
}

function page(items: Block[], pageNumber = 1, totalCount = items.length) {
  return HttpResponse.json({ items, page: pageNumber, pageSize: 20, totalCount });
}

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

const items = () => within(screen.getByRole("list", { name: "Bloqueios do campo" })).getAllByRole("listitem");
const text = (element: HTMLElement) => (element.textContent ?? "").replace(/\s/g, " ");

describe("BlocksSection", () => {
  it("says so when there are no ongoing or future blocks", async () => {
    server.use(http.get(url, () => page([])));

    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    expect(await screen.findByText("Sem bloqueios em curso nem futuros.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Novo bloqueio" })).toBeInTheDocument();
  });

  it("lists the blocks in Lisbon time with their reason, whatever the instants' zone", async () => {
    server.use(
      http.get(url, () =>
        page([
          block(1, { reason: "Manutenção do piso" }),
          // 22:30Z on 3 Jan is 22:30 in winter; the next day 00:30 is UTC too.
          block(2, { startsAt: "2099-01-03T22:30:00Z", endsAt: "2099-01-04T00:30:00Z" }),
        ]),
      ),
    );

    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    await screen.findByRole("list", { name: "Bloqueios do campo" });
    // 08:00Z in May is 09:00 WEST.
    expect(text(items()[0])).toContain("04/05/2099 09:00 – 04/05/2099 11:00");
    expect(text(items()[0])).toContain("Manutenção do piso");
    expect(text(items()[1])).toContain("03/01/2099 22:30 – 04/01/2099 00:30");
    expect(text(items()[1])).toContain("Sem motivo");
  });

  it("labels the repeated October hour in a block's times", async () => {
    server.use(http.get(url, () => page([block(1, { startsAt: "2099-10-25T00:30:00Z", endsAt: "2099-10-25T01:30:00Z" })])));

    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    await screen.findByRole("list", { name: "Bloqueios do campo" });
    expect(text(items()[0])).toContain("25/10/2099 01:30 (verão) – 25/10/2099 01:30 (inverno)");
  });

  it("marks an ongoing block and offers no removal for it", async () => {
    server.use(http.get(url, () => page([block(1, { startsAt: "2020-01-01T00:00:00Z", endsAt: "2099-01-01T00:00:00Z" }), block(2)])));

    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    await screen.findByRole("list", { name: "Bloqueios do campo" });
    expect(text(items()[0])).toContain("Em curso");
    expect(within(items()[0]).queryByRole("button", { name: /Remover/ })).not.toBeInTheDocument();
    expect(within(items()[1]).getByRole("button", { name: /Remover bloqueio/ })).toBeInTheDocument();
    expect(text(items()[1])).not.toContain("Em curso");
  });

  it("asks for confirmation before removing, and cancelling removes nothing", async () => {
    let deletes = 0;
    server.use(
      http.get(url, () => page([block(1)])),
      http.delete(`${url}/${block(1).id}`, () => (deletes++, new HttpResponse(null, { status: 204 }))),
    );
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Remover bloqueio/ }));
    const confirm = screen.getByRole("group", { name: "Confirmar remoção" });
    expect(confirm).toHaveTextContent("Remover este bloqueio?");
    expect(deletes).toBe(0);

    await user.click(within(confirm).getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("group", { name: "Confirmar remoção" })).not.toBeInTheDocument();
    expect(deletes).toBe(0);
    expect(screen.getByRole("button", { name: /Remover bloqueio/ })).toBeInTheDocument();
  });

  it("removes a block after confirmation, and the list and the preview on screen are refreshed", async () => {
    let removed = false;
    let slotCalls = 0;
    server.use(
      http.get(url, () => page(removed ? [] : [block(1)])),
      http.delete(`${url}/${block(1).id}`, () => {
        removed = true;
        return new HttpResponse(null, { status: 204 });
      }),
      http.get(`/api/manage/clubs/${clubId}/pitches/${pitchId}/slots`, () => {
        slotCalls++;
        return HttpResponse.json({
          date: "2099-05-04",
          slotDurationMinutes: 60,
          slots: [{ startsAt: "2099-05-04T08:00:00Z", endsAt: "2099-05-04T09:00:00Z", blocked: !removed }],
        });
      }),
    );
    renderWithProviders(
      <>
        <BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />
        <SlotPreview clubId={clubId} pitchId={pitchId} />
      </>,
    );
    const user = userEvent.setup();
    await screen.findByText("Bloqueado");

    await user.click(await screen.findByRole("button", { name: /Remover bloqueio/ }));
    await user.click(within(screen.getByRole("group", { name: "Confirmar remoção" })).getByRole("button", { name: "Confirmar remoção" }));

    expect(await screen.findByText("Sem bloqueios em curso nem futuros.")).toBeInTheDocument();
    expect((await screen.findAllByText("Bloqueio removido.")).length).toBeGreaterThan(0);
    await waitFor(() => expect(screen.queryByText("Bloqueado")).not.toBeInTheDocument());
    expect(slotCalls).toBeGreaterThanOrEqual(2);
  });

  it("explains a refusal (422 block_already_started) and shows the list as it is now", async () => {
    let started = false;
    server.use(
      http.get(url, () => page(started ? [block(1, { startsAt: "2020-01-01T00:00:00Z", endsAt: "2099-01-01T00:00:00Z" })] : [block(1)])),
      http.delete(`${url}/${block(1).id}`, () => {
        started = true;
        return problem(422, "block_already_started");
      }),
    );
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Remover bloqueio/ }));
    await user.click(within(screen.getByRole("group", { name: "Confirmar remoção" })).getByRole("button", { name: "Confirmar remoção" }));

    expect((await screen.findAllByText("Um bloqueio que já começou não pode ser removido.")).length).toBeGreaterThan(0);
    await waitFor(() => expect(text(items()[0])).toContain("Em curso"));
    expect(screen.queryByRole("group", { name: "Confirmar remoção" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Remover bloqueio/ })).not.toBeInTheDocument();
  });

  it("says so when the block is already gone (block_not_found)", async () => {
    let gone = false;
    server.use(
      http.get(url, () => page(gone ? [] : [block(1)])),
      http.delete(`${url}/${block(1).id}`, () => {
        gone = true;
        return problem(404, "block_not_found");
      }),
    );
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Remover bloqueio/ }));
    await user.click(within(screen.getByRole("group", { name: "Confirmar remoção" })).getByRole("button", { name: "Confirmar remoção" }));

    expect((await screen.findAllByText("Este bloqueio já não existe.")).length).toBeGreaterThan(0);
    expect(await screen.findByText("Sem bloqueios em curso nem futuros.")).toBeInTheDocument();
  });

  it("closes the panel when the pitch is gone (pitch_not_found)", async () => {
    onGone.mockReset();
    server.use(
      http.get(url, () => page([block(1)])),
      http.delete(`${url}/${block(1).id}`, () => problem(404, "pitch_not_found")),
    );
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: /Remover bloqueio/ }));
    await user.click(within(screen.getByRole("group", { name: "Confirmar remoção" })).getByRole("button", { name: "Confirmar remoção" }));

    await waitFor(() => expect(onGone).toHaveBeenCalled());
  });

  it("opens the form to add a block and closes it again", async () => {
    server.use(http.get(url, () => page([])));
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Novo bloqueio" }));
    expect(screen.getByRole("form", { name: "Criar bloqueio" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Novo bloqueio" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("form", { name: "Criar bloqueio" })).not.toBeInTheDocument();
  });

  it("shows an error with a way to try again when the list cannot be loaded", async () => {
    let calls = 0;
    server.use(http.get(url, () => (++calls === 1 ? problem(500, "internal_error") : page([block(1)]))));
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    await userEvent.setup().click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("list", { name: "Bloqueios do campo" })).toBeInTheDocument();
  });

  it("pages through more than 20 blocks", async () => {
    const asked: string[] = [];
    server.use(
      http.get(url, ({ request }) => {
        const requested = Number(new URL(request.url).searchParams.get("page"));
        asked.push(String(requested));
        return requested === 1 ? page([block(1, { reason: "primeiro" })], 1, 21) : page([block(21, { reason: "último" })], 2, 21);
      }),
    );
    renderWithProviders(<BlocksSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await screen.findByText("primeiro");
    await user.click(screen.getByRole("button", { name: "Seguinte" }));

    expect(await screen.findByText("último")).toBeInTheDocument();
    expect(asked).toEqual(["1", "2"]);
  });
});
