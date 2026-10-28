import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { Pitch } from "../api";
import { PitchSection } from "./pitch-section";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const listUrl = `/api/manage/clubs/${clubId}/pitches`;

function pitch(n: number, overrides: Partial<Pitch> = {}): Pitch {
  return {
    id: `0199f7a0-0000-7000-8000-${String(n).padStart(12, "0")}`,
    clubId,
    name: `Campo ${n}`,
    sport: "futsal",
    pricePerSlot: 12.5,
    status: "active",
    ...overrides,
  };
}

function page(items: Pitch[], pageNumber = 1, totalCount = items.length) {
  return HttpResponse.json({ items, page: pageNumber, pageSize: 20, totalCount });
}

function problem(status: number, code: string) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t" }, { status, headers: { "Content-Type": "application/problem+json" } });
}

// Only the pitches: the toaster renders list items too.
const pitchItems = () => within(screen.getByRole("list", { name: "Campos" })).getAllByRole("listitem");
const findPitchItems = async () => {
  await screen.findByRole("list", { name: "Campos" });
  return pitchItems();
};

const text = (element: HTMLElement) => (element.textContent ?? "").replace(/\s/g, " ");

describe("PitchSection", () => {
  it("shows an empty message and an add button when the club has no pitches", async () => {
    server.use(http.get(listUrl, () => page([])));

    renderWithProviders(<PitchSection clubId={clubId} />);

    expect(await screen.findByText(/ainda não tem campos/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Adicionar campo" })).toBeInTheDocument();
  });

  it("lists the pitches with the sport label and the price the pt-PT way", async () => {
    server.use(
      http.get(listUrl, () =>
        page([pitch(1), pitch(2, { sport: "football7", pricePerSlot: 8 }), pitch(3, { sport: "tennis", pricePerSlot: 0.5 }), pitch(4, { sport: "padel", pricePerSlot: 18.75 })]),
      ),
    );

    renderWithProviders(<PitchSection clubId={clubId} />);

    const items = await findPitchItems();
    expect(items.map((item) => text(item).replace(/Horário.*$/, "").trim())).toEqual([
      "Campo 1Futsal · 12,50 € por slot",
      "Campo 2Futebol 7 · 8,00 € por slot",
      "Campo 3Ténis · 0,50 € por slot",
      "Campo 4Padel · 18,75 € por slot",
    ]);
  });

  it("marks inactive pitches and offers to reactivate them", async () => {
    server.use(http.get(listUrl, () => page([pitch(1), pitch(2, { status: "inactive" })])));

    renderWithProviders(<PitchSection clubId={clubId} />);

    const [active, inactive] = await findPitchItems();
    expect(within(active).queryByText("Inativo")).not.toBeInTheDocument();
    expect(within(active).getByRole("button", { name: "Desativar Campo 1" })).toBeInTheDocument();
    expect(within(inactive).getByText("Inativo")).toBeInTheDocument();
    expect(within(inactive).getByRole("button", { name: "Reativar Campo 2" })).toBeInTheDocument();
    expect(within(inactive).queryByRole("button", { name: /^Desativar/ })).not.toBeInTheDocument();
  });

  it("deactivates a pitch and shows it as inactive", async () => {
    let status: Pitch["status"] = "active";
    let deactivated = "";
    server.use(
      http.get(listUrl, () => page([pitch(1, { status })])),
      http.post(`${listUrl}/${pitch(1).id}/deactivate`, ({ request }) => {
        deactivated = new URL(request.url).pathname;
        status = "inactive";
        return HttpResponse.json(pitch(1, { status }));
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);

    await userEvent.setup().click(await screen.findByRole("button", { name: "Desativar Campo 1" }));

    expect(await screen.findByText("Inativo")).toBeInTheDocument();
    expect(deactivated).toBe(`${listUrl}/${pitch(1).id}/deactivate`);
    expect(screen.getByRole("button", { name: "Reativar Campo 1" })).toBeInTheDocument();
    expect(await screen.findByText("«Campo 1» foi desativado.")).toBeInTheDocument();
  });

  it("reactivates an inactive pitch", async () => {
    let status: Pitch["status"] = "inactive";
    let reactivated = "";
    server.use(
      http.get(listUrl, () => page([pitch(1, { status })])),
      http.post(`${listUrl}/${pitch(1).id}/reactivate`, ({ request }) => {
        reactivated = new URL(request.url).pathname;
        status = "active";
        return HttpResponse.json(pitch(1, { status }));
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);

    await userEvent.setup().click(await screen.findByRole("button", { name: "Reativar Campo 1" }));

    expect(await screen.findByRole("button", { name: "Desativar Campo 1" })).toBeInTheDocument();
    expect(screen.queryByText("Inativo")).not.toBeInTheDocument();
    expect(reactivated).toBe(`${listUrl}/${pitch(1).id}/reactivate`);
    expect(await screen.findByText("«Campo 1» foi reativado.")).toBeInTheDocument();
  });

  it("says so, and refreshes the list, when the pitch to deactivate is gone (pitch_not_found)", async () => {
    let gone = false;
    server.use(
      http.get(listUrl, () => page(gone ? [] : [pitch(1)])),
      http.post(`${listUrl}/${pitch(1).id}/deactivate`, () => {
        gone = true;
        return problem(404, "pitch_not_found");
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);

    await userEvent.setup().click(await screen.findByRole("button", { name: "Desativar Campo 1" }));

    expect(await screen.findByText("Este campo já não existe neste clube.")).toBeInTheDocument();
    expect(await screen.findByText(/ainda não tem campos/i)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Campos" })).not.toBeInTheDocument();
  });

  it("shows a message for any other error when changing the status, and keeps the list", async () => {
    server.use(
      http.get(listUrl, () => page([pitch(1)])),
      http.post(`${listUrl}/${pitch(1).id}/deactivate`, () => problem(500, "internal_error")),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);

    await userEvent.setup().click(await screen.findByRole("button", { name: "Desativar Campo 1" }));

    expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Desativar Campo 1" })).toBeEnabled();
  });

  it("adds a pitch with the form and shows it in the list", async () => {
    const created = pitch(1, { name: "Campo Novo", sport: "padel", pricePerSlot: 20 });
    let added = false;
    server.use(
      http.get(listUrl, () => page(added ? [created] : [])),
      http.post(listUrl, async () => {
        added = true;
        return HttpResponse.json(created, { status: 201 });
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Adicionar campo" }));
    const form = screen.getByRole("form", { name: "Adicionar campo" });
    await user.type(within(form).getByLabelText(/nome do campo/i), "Campo Novo");
    await user.selectOptions(within(form).getByLabelText(/desporto/i), "padel");
    await user.type(within(form).getByLabelText(/preço por slot/i), "20");
    await user.click(within(form).getByRole("button", { name: "Adicionar campo" }));

    expect(await screen.findByText("Campo Novo")).toBeInTheDocument();
    expect(text(pitchItems()[0])).toContain("Padel · 20,00 € por slot");
    await waitFor(() => expect(screen.queryByRole("form")).not.toBeInTheDocument());
  });

  it("edits a pitch: the form opens with its values and the list shows the change", async () => {
    let current = pitch(1);
    server.use(
      http.get(listUrl, () => page([current])),
      http.put(`${listUrl}/${pitch(1).id}`, async ({ request }) => {
        const body = (await request.json()) as { name: string; sport: string; pricePerSlot: number };
        current = pitch(1, body as Partial<Pitch>);
        return HttpResponse.json(current);
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Editar Campo 1" }));
    const form = screen.getByRole("form", { name: "Editar campo" });
    expect(within(form).getByLabelText(/nome do campo/i)).toHaveValue("Campo 1");
    expect(within(form).getByLabelText(/preço por slot/i)).toHaveValue("12,50");
    await user.clear(within(form).getByLabelText(/preço por slot/i));
    await user.type(within(form).getByLabelText(/preço por slot/i), "15,5");
    await user.click(within(form).getByRole("button", { name: "Guardar campo" }));

    await waitFor(() => expect(text(pitchItems()[0])).toContain("15,50 € por slot"));
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
  });

  it("puts the duplicate name error on the name field when adding", async () => {
    server.use(
      http.get(listUrl, () => page([pitch(1)])),
      http.post(listUrl, () =>
        HttpResponse.json(
          { status: 400, title: "Dados inválidos", code: "validation_failed", traceId: "t", errors: { name: ["Já existe um campo com este nome neste clube."] } },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Adicionar campo" }));
    const form = screen.getByRole("form", { name: "Adicionar campo" });
    await user.type(within(form).getByLabelText(/nome do campo/i), "Campo 1");
    await user.selectOptions(within(form).getByLabelText(/desporto/i), "futsal");
    await user.type(within(form).getByLabelText(/preço por slot/i), "10");
    await user.click(within(form).getByRole("button", { name: "Adicionar campo" }));

    await waitFor(() => expect(within(form).getByLabelText(/nome do campo/i)).toHaveAccessibleDescription("Já existe um campo com este nome neste clube."));
    expect(screen.getByRole("form", { name: "Adicionar campo" })).toBeInTheDocument();
  });

  it("pages through more than 20 pitches", async () => {
    const asked: string[] = [];
    server.use(
      http.get(listUrl, ({ request }) => {
        const requested = Number(new URL(request.url).searchParams.get("page"));
        asked.push(String(requested));
        return requested === 1 ? page([pitch(1)], 1, 21) : page([pitch(21)], 2, 21);
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await screen.findByText("Campo 1");
    expect(screen.getByText("Página 1 de 2")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Seguinte" }));

    expect(await screen.findByText("Campo 21")).toBeInTheDocument();
    expect(asked).toEqual(["1", "2"]);
  });

  it("opens a pitch's schedule and preview below its row, one pitch at a time", async () => {
    const closedDay = { closed: true, opens: null, closes: null };
    server.use(
      http.get(listUrl, () => page([pitch(1), pitch(2)])),
      http.get(`${listUrl}/${pitch(1).id}/schedule`, () =>
        HttpResponse.json({ slotDurationMinutes: 60, monday: closedDay, tuesday: closedDay, wednesday: closedDay, thursday: closedDay, friday: closedDay, saturday: closedDay, sunday: closedDay }),
      ),
      http.get(`${listUrl}/${pitch(2).id}/schedule`, () =>
        HttpResponse.json({ slotDurationMinutes: 90, monday: closedDay, tuesday: closedDay, wednesday: closedDay, thursday: closedDay, friday: closedDay, saturday: closedDay, sunday: closedDay }),
      ),
      http.get(`${listUrl}/${pitch(1).id}/slots`, () => HttpResponse.json({ date: "2026-10-25", slotDurationMinutes: 60, slots: [] })),
      http.get(`${listUrl}/${pitch(2).id}/slots`, () => HttpResponse.json({ date: "2026-10-25", slotDurationMinutes: 90, slots: [] })),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Horário de Campo 1" }));
    expect(await screen.findByRole("heading", { name: "Horário de Campo 1" })).toBeInTheDocument();
    expect(await screen.findByRole("form", { name: "Horário do campo" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Pré-visualização dos slots" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Horário de Campo 1" })).toHaveAttribute("aria-expanded", "true");

    await user.click(screen.getByRole("button", { name: "Horário de Campo 2" }));
    expect(await screen.findByRole("heading", { name: "Horário de Campo 2" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Horário de Campo 1" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Horário de Campo 2" }));
    expect(screen.queryByRole("form", { name: "Horário do campo" })).not.toBeInTheDocument();
  });

  it("opens a pitch's blocks below its row and swaps panels with the schedule", async () => {
    const closedDay = { closed: true, opens: null, closes: null };
    server.use(
      http.get(listUrl, () => page([pitch(1)])),
      http.get(`${listUrl}/${pitch(1).id}/blocks`, () => HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 })),
      http.get(`${listUrl}/${pitch(1).id}/schedule`, () =>
        HttpResponse.json({ slotDurationMinutes: 60, monday: closedDay, tuesday: closedDay, wednesday: closedDay, thursday: closedDay, friday: closedDay, saturday: closedDay, sunday: closedDay }),
      ),
      http.get(`${listUrl}/${pitch(1).id}/slots`, () => HttpResponse.json({ date: "2026-10-25", slotDurationMinutes: 60, slots: [] })),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Bloqueios de Campo 1" }));
    expect(await screen.findByRole("heading", { name: "Bloqueios de Campo 1" })).toBeInTheDocument();
    expect(await screen.findByText("Sem bloqueios em curso nem futuros.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bloqueios de Campo 1" })).toHaveAttribute("aria-expanded", "true");

    await user.click(screen.getByRole("button", { name: "Horário de Campo 1" }));
    expect(await screen.findByRole("heading", { name: "Horário de Campo 1" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Bloqueios de Campo 1" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bloqueios de Campo 1" })).toHaveAttribute("aria-expanded", "false");

    await user.click(screen.getByRole("button", { name: "Horário de Campo 1" }));
    expect(screen.queryByRole("heading", { name: "Horário de Campo 1" })).not.toBeInTheDocument();
  });

  it("opens a pitch's price bands next to Horário and Bloqueios, one panel at a time (US10)", async () => {
    const id = pitch(1).id;
    server.use(
      http.get(listUrl, () => page([pitch(1)])),
      http.get(`${listUrl}/${id}/price-bands`, () =>
        HttpResponse.json([{ id: "b1", pitchId: id, days: ["monday"], start: "18:00", end: "22:00", price: 30 }]),
      ),
      http.get(`${listUrl}/${id}/blocks`, () => HttpResponse.json({ items: [], page: 1, pageSize: 20, totalCount: 0 })),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Faixas de preço de Campo 1" }));
    expect(await screen.findByRole("heading", { name: "Faixas de preço de Campo 1" })).toBeInTheDocument();
    expect(await screen.findByText(/18:00–22:00/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Faixas de preço de Campo 1" })).toHaveAttribute("aria-expanded", "true");

    await user.click(screen.getByRole("button", { name: "Bloqueios de Campo 1" }));
    expect(await screen.findByRole("heading", { name: "Bloqueios de Campo 1" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Faixas de preço de Campo 1" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Faixas de preço de Campo 1" })).toHaveAttribute("aria-expanded", "false");
  });

  it("shows an error with a way to try again when the list cannot be loaded", async () => {
    let calls = 0;
    server.use(
      http.get(listUrl, () => {
        calls++;
        return calls === 1 ? problem(500, "internal_error") : page([pitch(1)]);
      }),
    );
    renderWithProviders(<PitchSection clubId={clubId} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    await userEvent.setup().click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByText("Campo 1")).toBeInTheDocument();
  });
});
