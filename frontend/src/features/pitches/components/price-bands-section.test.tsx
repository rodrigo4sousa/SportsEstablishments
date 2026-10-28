import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { PriceBand } from "../api";
import { PriceBandsSection } from "./price-bands-section";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const pitchId = "0199f7a0-0000-7000-8000-0000000000d1";
const url = `/api/manage/clubs/${clubId}/pitches/${pitchId}/price-bands`;
const onGone = vi.fn();

function band(n: number, overrides: Partial<PriceBand> = {}): PriceBand {
  return {
    id: `0199f7a0-0000-7000-8000-${String(n).padStart(12, "0")}`,
    pitchId,
    days: ["monday", "friday"],
    start: "18:00",
    end: "22:00",
    price: 30,
    ...overrides,
  };
}

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) }, { status, headers: { "Content-Type": "application/problem+json" } });
}

const items = () => within(screen.getByRole("list", { name: "Faixas de preço do campo" })).getAllByRole("listitem");
const text = (element: HTMLElement) => (element.textContent ?? "").replace(/\s/g, " ");

/** Serves the list and records what is posted; the list answers with `bands` as it is when asked. */
function serve(bands: PriceBand[] = []) {
  const posted: unknown[] = [];
  server.use(
    http.get(url, () => HttpResponse.json(bands)),
    http.post(url, async ({ request }) => {
      const body = (await request.json()) as Omit<PriceBand, "id" | "pitchId">;
      posted.push(body);
      const created = band(90 + posted.length, body);
      bands.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
  );
  return { bands, posted };
}

async function openNewForm() {
  const user = userEvent.setup();
  await user.click(await screen.findByRole("button", { name: "Nova faixa" }));
  return { user, form: screen.getByRole("form", { name: "Criar faixa de preço" }) };
}

describe("PriceBandsSection", () => {
  it("says a pitch without bands charges the base price", async () => {
    serve();

    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    expect(await screen.findByText(/todos os slots custam o preço base/)).toBeInTheDocument();
  });

  it("lists each band with its weekdays (pt-PT, Monday first), hours and price", async () => {
    serve([band(1, { days: ["sunday", "friday", "monday"] }), band(2, { days: ["saturday"], start: "22:00", end: "00:00", price: 35.5 })]);

    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    await screen.findByRole("list", { name: "Faixas de preço do campo" });
    expect(text(items()[0])).toContain("18:00–22:00 · 30,00 €");
    expect(text(items()[0])).toContain("Segunda-feira, Sexta-feira, Domingo");
    // A band that ends at midnight reads 24:00, not an empty range.
    expect(text(items()[1])).toContain("22:00–24:00 · 35,50 €");
    expect(text(items()[1])).toContain("Sábado");
  });

  it("offers the weekdays Monday first and sends the chosen ones with the times and the price as a number", async () => {
    const { posted } = serve();
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const { user, form } = await openNewForm();

    const days = within(form).getAllByRole("checkbox").map((box) => (box as HTMLInputElement).value);
    expect(days).toEqual(["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"]);
    expect(within(form).getByRole("checkbox", { name: "Segunda-feira" })).not.toBeChecked();
    await user.click(within(form).getByRole("checkbox", { name: "Sexta-feira" }));
    await user.click(within(form).getByRole("checkbox", { name: "Segunda-feira" }));
    await user.type(within(form).getByLabelText("Início"), "18:00");
    await user.type(within(form).getByLabelText("Fim"), "22:00");
    await user.type(within(form).getByLabelText("Preço por slot (€)"), "30,5");
    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));

    await waitFor(() => expect(posted).toEqual([{ days: ["monday", "friday"], start: "18:00", end: "22:00", price: 30.5 }]));
    // The form closes and the list shows the new band.
    await waitFor(() => expect(screen.queryByRole("form", { name: "Criar faixa de preço" })).not.toBeInTheDocument());
    expect(text((await screen.findAllByRole("listitem"))[0])).toContain("18:00–22:00 · 30,50 €");
  });

  it("checks the form before sending: no weekday, an end before the start, an off-step time and a bad price", async () => {
    const { posted } = serve();
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const { user, form } = await openNewForm();

    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));
    expect(await within(form).findByText("Escolhe pelo menos um dia da semana.")).toBeInTheDocument();
    expect(within(form).getByText("Indica a hora de início da faixa.")).toBeInTheDocument();
    expect(within(form).getByText("Indica a hora de fim da faixa.")).toBeInTheDocument();
    expect(within(form).getByText("Indica o preço por slot.")).toBeInTheDocument();

    await user.type(within(form).getByLabelText("Início"), "22:00");
    await user.type(within(form).getByLabelText("Fim"), "18:00");
    await user.type(within(form).getByLabelText("Preço por slot (€)"), "0");
    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));
    expect(await within(form).findByText(/O fim tem de ser depois do início/)).toBeInTheDocument();
    expect(within(form).getByText("O preço por slot tem de ser maior que 0 €.")).toBeInTheDocument();

    // An end of 00:00 is midnight, so it is after any start.
    await user.clear(within(form).getByLabelText("Fim"));
    await user.type(within(form).getByLabelText("Fim"), "00:00");
    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));
    await waitFor(() => expect(screen.queryByText(/O fim tem de ser depois do início/)).not.toBeInTheDocument());
    expect(posted).toEqual([]); // the price is still invalid
  });

  it("puts the API's field messages on their fields", async () => {
    server.use(
      http.get(url, () => HttpResponse.json([])),
      http.post(url, () => problem(400, "validation_failed", { days: ["Escolhe pelo menos um dia da semana."], price: ["O preço não pode passar de 500 €."] })),
    );
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const { user, form } = await openNewForm();
    await user.click(within(form).getByRole("checkbox", { name: "Sábado" }));
    await user.type(within(form).getByLabelText("Início"), "18:00");
    await user.type(within(form).getByLabelText("Fim"), "22:00");
    await user.type(within(form).getByLabelText("Preço por slot (€)"), "30");
    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));

    expect(await within(form).findByText("O preço não pode passar de 500 €.")).toBeInTheDocument();
    expect(within(form).getByText("Escolhe pelo menos um dia da semana.")).toBeInTheDocument();
    expect(within(form).getByLabelText("Preço por slot (€)")).toHaveAttribute("aria-invalid", "true");
  });

  it("shows an overlap above the buttons, not on a field, and keeps what was typed", async () => {
    server.use(
      http.get(url, () => HttpResponse.json([band(1)])),
      http.post(url, () => problem(409, "price_band_overlap")),
    );
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const { user, form } = await openNewForm();
    await user.click(within(form).getByRole("checkbox", { name: "Segunda-feira" }));
    await user.type(within(form).getByLabelText("Início"), "19:00");
    await user.type(within(form).getByLabelText("Fim"), "21:00");
    await user.type(within(form).getByLabelText("Preço por slot (€)"), "35");
    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));

    const alert = await within(form).findByRole("alert");
    expect(alert).toHaveTextContent("Esta faixa sobrepõe-se a outra faixa de preço deste campo num dos dias escolhidos.");
    expect(within(form).getByLabelText("Início")).toHaveValue("19:00");
    expect(within(form).getByLabelText("Início")).not.toHaveAttribute("aria-invalid");
    expect(within(form).getByRole("checkbox", { name: "Segunda-feira" })).toBeChecked();

    // Changing a field takes the message away.
    await user.clear(within(form).getByLabelText("Início"));
    await user.type(within(form).getByLabelText("Início"), "20:00");
    expect(within(form).queryByRole("alert")).not.toBeInTheDocument();
  });

  it("says when the pitch is at the limit of bands", async () => {
    server.use(
      http.get(url, () => HttpResponse.json([band(1)])),
      http.post(url, () => problem(422, "price_band_limit_reached")),
    );
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const { user, form } = await openNewForm();
    await user.click(within(form).getByRole("checkbox", { name: "Terça-feira" }));
    await user.type(within(form).getByLabelText("Início"), "10:00");
    await user.type(within(form).getByLabelText("Fim"), "11:00");
    await user.type(within(form).getByLabelText("Preço por slot (€)"), "20");
    await user.click(within(form).getByRole("button", { name: "Criar faixa" }));

    expect(await within(form).findByRole("alert")).toHaveTextContent("Um campo pode ter no máximo 20 faixas de preço.");
  });

  it("edits a band in place: the form starts with its values and sends them to that band", async () => {
    const put: unknown[] = [];
    const bands = [band(1)];
    server.use(
      http.get(url, () => HttpResponse.json(bands)),
      http.put(`${url}/${bands[0].id}`, async ({ request }) => {
        put.push(await request.json());
        return HttpResponse.json({ ...bands[0], price: 33 });
      }),
    );
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Editar faixa 18:00–22:00" }));

    const form = screen.getByRole("form", { name: "Editar faixa de preço" });
    expect(within(form).getByRole("checkbox", { name: "Segunda-feira" })).toBeChecked();
    expect(within(form).getByRole("checkbox", { name: "Sexta-feira" })).toBeChecked();
    expect(within(form).getByRole("checkbox", { name: "Terça-feira" })).not.toBeChecked();
    expect(within(form).getByLabelText("Início")).toHaveValue("18:00");
    expect(within(form).getByLabelText("Preço por slot (€)")).toHaveValue("30,00");
    await user.clear(within(form).getByLabelText("Preço por slot (€)"));
    await user.type(within(form).getByLabelText("Preço por slot (€)"), "33");
    await user.click(within(form).getByRole("button", { name: "Guardar faixa" }));

    await waitFor(() => expect(put).toEqual([{ days: ["monday", "friday"], start: "18:00", end: "22:00", price: 33 }]));
  });

  it("asks for a confirmation before deleting, and the cancel button leaves the band alone", async () => {
    let deleted = 0;
    const bands = [band(1)];
    server.use(
      http.get(url, () => HttpResponse.json(bands)),
      http.delete(`${url}/${bands[0].id}`, () => {
        deleted += 1;
        bands.length = 0;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Remover faixa 18:00–22:00" }));
    const group = screen.getByRole("group", { name: "Confirmar remoção" });
    expect(group).toHaveTextContent("As reservas já feitas mantêm o preço.");
    expect(deleted).toBe(0);
    await user.click(within(group).getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("group", { name: "Confirmar remoção" })).not.toBeInTheDocument();
    expect(deleted).toBe(0);

    await user.click(screen.getByRole("button", { name: "Remover faixa 18:00–22:00" }));
    await user.click(within(screen.getByRole("group", { name: "Confirmar remoção" })).getByRole("button", { name: "Confirmar remoção" }));

    await waitFor(() => expect(deleted).toBe(1));
    expect(await screen.findByText(/todos os slots custam o preço base/)).toBeInTheDocument();
  });

  it("reloads the list when the band was already gone", async () => {
    const bands = [band(1)];
    server.use(
      http.get(url, () => HttpResponse.json(bands)),
      http.delete(`${url}/${bands[0].id}`, () => {
        bands.length = 0;
        return problem(404, "price_band_not_found");
      }),
    );
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Remover faixa 18:00–22:00" }));
    await user.click(within(screen.getByRole("group", { name: "Confirmar remoção" })).getByRole("button", { name: "Confirmar remoção" }));

    expect(await screen.findByText(/todos os slots custam o preço base/)).toBeInTheDocument();
  });

  it("shows a message with a retry when the bands cannot be loaded", async () => {
    let fail = true;
    server.use(http.get(url, () => (fail ? problem(500, "internal_error") : HttpResponse.json([]))));
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);
    const user = userEvent.setup();

    const alert = await screen.findByRole("alert");
    fail = false;
    await user.click(within(alert).getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByText(/todos os slots custam o preço base/)).toBeInTheDocument();
  });

  it("turns the new-band button off at twenty bands", async () => {
    serve(Array.from({ length: 20 }, (_, index) => band(index + 1, { days: ["tuesday"], start: "10:00", end: "11:00" })));
    renderWithProviders(<PriceBandsSection clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    await screen.findByRole("list", { name: "Faixas de preço do campo" });
    expect(screen.getByRole("button", { name: "Nova faixa" })).toBeDisabled();
    expect(screen.getByText("Um campo pode ter no máximo 20 faixas.")).toBeInTheDocument();
  });
});
