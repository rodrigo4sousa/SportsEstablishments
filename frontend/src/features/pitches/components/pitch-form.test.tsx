import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { Pitch } from "../api";
import { PitchForm } from "./pitch-form";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const onDone = vi.fn();

const saved: Pitch = {
  id: "0199f7a0-0000-7000-8000-0000000000d1",
  clubId,
  name: "Campo 1",
  sport: "futsal",
  pricePerSlot: 12.5,
  status: "active",
};

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

const field = (name: RegExp) => screen.getByLabelText(name);

async function fillIn(values: { name?: string; sport?: string; price?: string }) {
  const user = userEvent.setup();
  if (values.name !== undefined) {
    await user.clear(field(/nome do campo/i));
    if (values.name) await user.type(field(/nome do campo/i), values.name);
  }
  if (values.sport !== undefined) await user.selectOptions(field(/desporto/i), values.sport);
  if (values.price !== undefined) {
    await user.clear(field(/preço por slot/i));
    if (values.price) await user.type(field(/preço por slot/i), values.price);
  }
  return user;
}

function expectFieldError(name: RegExp, message: string) {
  expect(field(name)).toHaveAttribute("aria-invalid", "true");
  expect(field(name)).toHaveAccessibleDescription(expect.stringContaining(message));
}

describe("PitchForm (new pitch)", () => {
  beforeEach(() => {
    onDone.mockReset();
  });

  it("offers the four sports with their pt-PT labels", () => {
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);

    const options = Array.from((field(/desporto/i) as HTMLSelectElement).options).map((option) => [option.value, option.text]);
    expect(options).toEqual([
      ["", "Escolhe o desporto"],
      ["futsal", "Futsal"],
      ["football7", "Futebol 7"],
      ["padel", "Padel"],
      ["tennis", "Ténis"],
    ]);
  });

  it("shows one message under each empty field and does not call the API", async () => {
    let requests = 0;
    server.use(
      http.post(`/api/manage/clubs/${clubId}/pitches`, () => {
        requests++;
        return HttpResponse.json(saved, { status: 201 });
      }),
    );
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Adicionar campo" }));

    expectFieldError(/nome do campo/i, "Indica o nome do campo.");
    expectFieldError(/desporto/i, "Indica o desporto do campo.");
    expectFieldError(/preço por slot/i, "Indica o preço por slot.");
    expect(requests).toBe(0);
    expect(onDone).not.toHaveBeenCalled();
  });

  it.each([
    ["0", "O preço por slot tem de ser maior que 0 €."],
    ["12,555", "O preço por slot pode ter no máximo duas casas decimais."],
    ["500,01", "O preço por slot não pode passar de 500 €."],
    ["abc", "Indica um preço válido, por exemplo 12,50."],
  ])("rejects the price %j on the price field only", async (price, message) => {
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);
    const user = await fillIn({ name: "Campo 1", sport: "futsal", price });

    await user.click(screen.getByRole("button", { name: "Adicionar campo" }));

    expectFieldError(/preço por slot/i, message);
    expect(field(/nome do campo/i)).not.toHaveAttribute("aria-invalid");
    expect(field(/desporto/i)).not.toHaveAttribute("aria-invalid");
  });

  it("rejects a name over 100 characters", async () => {
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);
    const user = await fillIn({ sport: "padel", price: "10" });
    await user.click(field(/nome do campo/i));
    await user.paste("a".repeat(101));

    await user.click(screen.getByRole("button", { name: "Adicionar campo" }));

    expectFieldError(/nome do campo/i, "O nome pode ter no máximo 100 caracteres.");
  });

  it.each([
    ["12,50", 12.5],
    ["12.50", 12.5],
    ["12", 12],
    ["12,5 €", 12.5],
  ])("sends the price %j to the API as the number %d", async (typed, sent) => {
    let body: unknown;
    server.use(
      http.post(`/api/manage/clubs/${clubId}/pitches`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...saved, pricePerSlot: sent }, { status: 201 });
      }),
    );
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);
    const user = await fillIn({ name: "  Campo 1 ", sport: "football7", price: typed });

    await user.click(screen.getByRole("button", { name: "Adicionar campo" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    expect(body).toEqual({ name: "Campo 1", sport: "football7", pricePerSlot: sent });
    expect((await screen.findAllByText("Campo adicionado.")).length).toBeGreaterThan(0);
  });

  it("puts the API's duplicate-name error on the name field and stays open", async () => {
    server.use(
      http.post(`/api/manage/clubs/${clubId}/pitches`, () =>
        problem(400, "validation_failed", { name: ["Já existe um campo com este nome neste clube."] }),
      ),
    );
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);
    const user = await fillIn({ name: "Campo 1", sport: "futsal", price: "12,50" });

    await user.click(screen.getByRole("button", { name: "Adicionar campo" }));

    await waitFor(() => expectFieldError(/nome do campo/i, "Já existe um campo com este nome neste clube."));
    expect(field(/nome do campo/i)).toHaveFocus();
    expect(onDone).not.toHaveBeenCalled();
    // The values typed are still there to correct.
    expect(field(/preço por slot/i)).toHaveValue("12,50");
  });

  it("maps API errors on the sport and price fields too", async () => {
    server.use(
      http.post(`/api/manage/clubs/${clubId}/pitches`, () =>
        problem(400, "validation_failed", {
          sport: ["Escolhe um desporto da lista: futsal, futebol 7, padel ou ténis."],
          pricePerSlot: ["O preço por slot não pode passar de 500 €."],
        }),
      ),
    );
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);
    const user = await fillIn({ name: "Campo 1", sport: "padel", price: "20" });

    await user.click(screen.getByRole("button", { name: "Adicionar campo" }));

    await waitFor(() => expectFieldError(/desporto/i, "Escolhe um desporto da lista"));
    expectFieldError(/preço por slot/i, "O preço por slot não pode passar de 500 €.");
  });

  it("shows a toast for an unexpected error and keeps the form", async () => {
    server.use(http.post(`/api/manage/clubs/${clubId}/pitches`, () => problem(500, "internal_error")));
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);
    const user = await fillIn({ name: "Campo 1", sport: "futsal", price: "12,50" });

    await user.click(screen.getByRole("button", { name: "Adicionar campo" }));

    expect(await screen.findByText("Ocorreu um erro inesperado. Tenta novamente mais tarde.")).toBeInTheDocument();
    expect(onDone).not.toHaveBeenCalled();
  });

  it("cancels without calling the API", async () => {
    renderWithProviders(<PitchForm clubId={clubId} onDone={onDone} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Cancelar" }));

    expect(onDone).toHaveBeenCalledTimes(1);
  });
});

describe("PitchForm (existing pitch)", () => {
  beforeEach(() => {
    onDone.mockReset();
  });

  it("starts with the pitch's values, the price with a comma", () => {
    renderWithProviders(<PitchForm clubId={clubId} pitch={saved} onDone={onDone} />);

    expect(field(/nome do campo/i)).toHaveValue("Campo 1");
    expect(field(/desporto/i)).toHaveValue("futsal");
    expect(field(/preço por slot/i)).toHaveValue("12,50");
    expect(screen.getByRole("form", { name: "Editar campo" })).toBeInTheDocument();
  });

  it("saves the changes with PUT and the price as a number", async () => {
    let seen: { url: string; body: unknown } | undefined;
    server.use(
      http.put(`/api/manage/clubs/${clubId}/pitches/${saved.id}`, async ({ request }) => {
        seen = { url: new URL(request.url).pathname, body: await request.json() };
        return HttpResponse.json({ ...saved, name: "Campo Central", sport: "padel", pricePerSlot: 20 });
      }),
    );
    renderWithProviders(<PitchForm clubId={clubId} pitch={saved} onDone={onDone} />);
    const user = await fillIn({ name: "Campo Central", sport: "padel", price: "20,00" });

    await user.click(screen.getByRole("button", { name: "Guardar campo" }));

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    expect(seen).toEqual({
      url: `/api/manage/clubs/${clubId}/pitches/${saved.id}`,
      body: { name: "Campo Central", sport: "padel", pricePerSlot: 20 },
    });
    expect(await screen.findByText("Alterações guardadas.")).toBeInTheDocument();
  });

  it("tells the owner when the pitch no longer exists (pitch_not_found) and closes the form", async () => {
    server.use(http.put(`/api/manage/clubs/${clubId}/pitches/${saved.id}`, () => problem(404, "pitch_not_found")));
    renderWithProviders(<PitchForm clubId={clubId} pitch={saved} onDone={onDone} />);
    const user = await fillIn({ name: "Outro nome" });

    await user.click(screen.getByRole("button", { name: "Guardar campo" }));

    expect(await screen.findByText("Este campo já não existe neste clube.")).toBeInTheDocument();
    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
  });
});
