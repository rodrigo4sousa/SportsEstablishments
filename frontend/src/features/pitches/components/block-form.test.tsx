import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { BlockForm } from "./block-form";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const pitchId = "0199f7a0-0000-7000-8000-0000000000d1";
const url = `/api/manage/clubs/${clubId}/pitches/${pitchId}/blocks`;
const onDone = vi.fn();

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

const created = { id: "0199f7a0-0000-7000-8000-0000000000e1", pitchId, startsAt: "2026-05-04T08:00:00Z", endsAt: "2026-05-04T10:00:00Z", reason: null };

const field = (label: string) => screen.getByLabelText(label);

function fill(values: Partial<Record<"startDate" | "startTime" | "endDate" | "endTime", string>> & { reason?: string }) {
  const labels = { startDate: "Início: data", startTime: "Início: hora", endDate: "Fim: data", endTime: "Fim: hora" } as const;
  for (const [key, label] of Object.entries(labels) as Array<[keyof typeof labels, string]>) {
    if (values[key] !== undefined) fireEvent.change(field(label), { target: { value: values[key] } });
  }
  if (values.reason !== undefined) fireEvent.change(field("Motivo (opcional)"), { target: { value: values.reason } });
}

const submit = () => userEvent.setup().click(screen.getByRole("button", { name: "Criar bloqueio" }));

function expectError(label: string, message: string) {
  expect(field(label)).toHaveAttribute("aria-invalid", "true");
  expect(field(label)).toHaveAccessibleDescription(expect.stringContaining(message));
}

describe("BlockForm", () => {
  beforeEach(() => {
    onDone.mockReset();
    // 08:00 UTC on 4 May 2026 is 09:00 in Lisbon (WEST).
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-04T08:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("asks for both periods and does not call the API when they are empty", async () => {
    let calls = 0;
    server.use(http.post(url, () => (calls++, HttpResponse.json(created, { status: 201 }))));
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);

    await submit();

    expectError("Início: data", "Indica o início do bloqueio.");
    expectError("Fim: data", "Indica o fim do bloqueio.");
    expect(calls).toBe(0);
    expect(onDone).not.toHaveBeenCalled();
  });

  it("asks for the time when only the date is given", async () => {
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", endDate: "2026-05-05" });

    await submit();

    expectError("Início: hora", "Indica a hora de início.");
    expectError("Fim: hora", "Indica a hora de fim.");
  });

  it("wants times in 30-minute steps", async () => {
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:15", endDate: "2026-05-05", endTime: "10:00" });

    await submit();

    expectError("Início: hora", "As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.");
    expect(field("Fim: hora")).not.toHaveAttribute("aria-invalid");
  });

  it("refuses a start in the past, read in Lisbon time", async () => {
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    // It is 09:00 in Lisbon: 08:30 is past, even though 08:30 is after 08:00 (the UTC clock).
    fill({ startDate: "2026-05-04", startTime: "08:30", endDate: "2026-05-04", endTime: "12:00" });

    await submit();

    expectError("Início: data", "O início não pode estar no passado.");
  });

  it("accepts a start right now", async () => {
    server.use(http.post(url, () => HttpResponse.json(created, { status: 201 })));
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-04", startTime: "09:00", endDate: "2026-05-04", endTime: "10:00" });

    await submit();

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
  });

  it.each([
    ["2026-05-05", "10:00", "2026-05-05", "09:00"],
    ["2026-05-05", "10:00", "2026-05-05", "10:00"],
    ["2026-05-06", "09:00", "2026-05-05", "12:00"],
  ])("refuses an end that is not after the start (%s %s to %s %s)", async (startDate, startTime, endDate, endTime) => {
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate, startTime, endDate, endTime });

    await submit();

    expectError("Fim: data", "O fim tem de ser depois do início.");
    expect(field("Início: data")).not.toHaveAttribute("aria-invalid");
  });

  it("refuses a reason over 200 characters", async () => {
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:00", endDate: "2026-05-05", endTime: "10:00", reason: "a".repeat(201) });

    await submit();

    expectError("Motivo (opcional)", "O motivo pode ter no máximo 200 caracteres.");
  });

  it("sends Lisbon local date-times, may span days, and sends no reason when it is empty", async () => {
    let body: unknown;
    server.use(
      http.post(url, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(created, { status: 201 });
      }),
    );
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "22:00", endDate: "2026-05-07", endTime: "02:30", reason: "  " });

    await submit();

    await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    expect(body).toEqual({ start: "2026-05-05T22:00", end: "2026-05-07T02:30", reason: null });
    expect((await screen.findAllByText("Bloqueio criado.")).length).toBeGreaterThan(0);
  });

  it("sends the reason, trimmed", async () => {
    let body: unknown;
    server.use(
      http.post(url, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(created, { status: 201 });
      }),
    );
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:00", endDate: "2026-05-05", endTime: "10:00", reason: " Piso novo " });

    await submit();

    await waitFor(() => expect(onDone).toHaveBeenCalled());
    expect(body).toEqual({ start: "2026-05-05T09:00", end: "2026-05-05T10:00", reason: "Piso novo" });
  });

  it("puts the API's past-start error on the start field", async () => {
    server.use(http.post(url, () => problem(400, "validation_failed", { start: ["O início não pode estar no passado."] })));
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:00", endDate: "2026-05-05", endTime: "10:00" });

    await submit();

    await waitFor(() => expectError("Início: data", "Verifica o início: não pode estar no passado e vai de meia em meia hora."));
    expect(field("Fim: data")).not.toHaveAttribute("aria-invalid");
    expect(onDone).not.toHaveBeenCalled();
  });

  it("puts the API's end and reason errors on their fields", async () => {
    server.use(
      http.post(url, () =>
        problem(400, "validation_failed", {
          end: ["O bloqueio não pode acabar daqui a mais de 90 dias."],
          reason: ["O motivo pode ter no máximo 200 caracteres."],
        }),
      ),
    );
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:00", endDate: "2026-12-05", endTime: "10:00" });

    await submit();

    await waitFor(() => expectError("Fim: data", "Verifica o fim: depois do início, no máximo 90 dias à frente e de meia em meia hora."));
    expectError("Motivo (opcional)", "O motivo pode ter no máximo 200 caracteres.");
  });

  it("shows an overlap with another block above the buttons, keeps the form, and clears the message on edit", async () => {
    server.use(http.post(url, () => problem(409, "block_overlap")));
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:00", endDate: "2026-05-05", endTime: "10:00" });

    await submit();

    expect(await screen.findByRole("alert")).toHaveTextContent("Este período sobrepõe-se a outro bloqueio deste campo.");
    expect(field("Início: hora")).toHaveValue("09:00");
    expect(onDone).not.toHaveBeenCalled();

    fill({ endTime: "09:30" });
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
  });

  it("tells the owner when the pitch is gone (pitch_not_found) and closes the form", async () => {
    server.use(http.post(url, () => problem(404, "pitch_not_found")));
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);
    fill({ startDate: "2026-05-05", startTime: "09:00", endDate: "2026-05-05", endTime: "10:00" });

    await submit();

    expect((await screen.findAllByText("Este campo já não existe neste clube.")).length).toBeGreaterThan(0);
    await waitFor(() => expect(onDone).toHaveBeenCalled());
  });

  it("cancels without calling the API", async () => {
    renderWithProviders(<BlockForm clubId={clubId} pitchId={pitchId} onDone={onDone} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Cancelar" }));

    expect(onDone).toHaveBeenCalledTimes(1);
  });
});
