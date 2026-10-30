import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { DayHours, PitchSchedule } from "../api";
import { ScheduleEditor } from "./schedule-editor";

const clubId = "0199f7a0-0000-7000-8000-0000000000c1";
const pitchId = "0199f7a0-0000-7000-8000-0000000000d1";
const url = `/api/manage/clubs/${clubId}/pitches/${pitchId}/schedule`;
const onGone = vi.fn();

const closed: DayHours = { closed: true, opens: null, closes: null };
const open = (opens: string, closes: string): DayHours => ({ closed: false, opens, closes });

function schedule(overrides: Partial<PitchSchedule> = {}): PitchSchedule {
  return {
    slotDurationMinutes: 60,
    monday: closed,
    tuesday: closed,
    wednesday: closed,
    thursday: closed,
    friday: closed,
    saturday: closed,
    sunday: closed,
    ...overrides,
  };
}

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "t", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

const row = (name: string) => screen.getByRole("group", { name });
const opens = (day: string) => within(row(day)).getByLabelText(`${day}: abertura`);
const closes = (day: string) => within(row(day)).getByLabelText(`${day}: fecho`);

async function loaded(initial = schedule()) {
  server.use(http.get(url, () => HttpResponse.json(initial)));
  const result = renderWithProviders(<ScheduleEditor clubId={clubId} pitchId={pitchId} onGone={onGone} />);
  await screen.findByRole("form", { name: "Horário do campo" });
  return { ...result, user: userEvent.setup() };
}

async function setTime(user: ReturnType<typeof userEvent.setup>, input: HTMLElement, value: string) {
  await user.clear(input);
  await user.type(input, value);
}

describe("ScheduleEditor", () => {
  beforeEach(() => {
    onGone.mockReset();
  });

  it("shows the seven days, Monday first, with pt-PT names, all closed for a new pitch", async () => {
    await loaded();

    const names = screen.getAllByRole("group").map((group) => group.getAttribute("aria-label")).filter(Boolean);
    expect(names).toEqual(["Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado", "Domingo"]);
    for (const name of names) {
      expect(within(row(name!)).getByRole("checkbox", { name: "Fechado" })).toBeChecked();
      expect(within(row(name!)).queryByLabelText(/abertura/)).not.toBeInTheDocument();
    }
  });

  it("offers 30, 60 and 90 minutes for the slot length and starts with the pitch's", async () => {
    await loaded(schedule({ slotDurationMinutes: 90 }));

    const select = screen.getByLabelText("Duração do horário") as HTMLSelectElement;
    expect(Array.from(select.options).map((option) => [option.value, option.text])).toEqual([
      ["30", "30 minutos"],
      ["60", "60 minutos"],
      ["90", "90 minutos"],
    ]);
    expect(select).toHaveValue("90");
  });

  it("shows the saved hours of open days and hides the times of closed ones", async () => {
    await loaded(schedule({ monday: open("09:00", "22:00"), saturday: open("08:30", "00:00") }));

    expect(opens("Segunda-feira")).toHaveValue("09:00");
    expect(closes("Segunda-feira")).toHaveValue("22:00");
    expect(opens("Sábado")).toHaveValue("08:30");
    expect(closes("Sábado")).toHaveValue("00:00");
    expect(within(row("Segunda-feira")).getByRole("checkbox", { name: "Fechado" })).not.toBeChecked();
    expect(within(row("Terça-feira")).queryByLabelText(/abertura/)).not.toBeInTheDocument();
  });

  it("opening a closed day offers usual hours, and closing it again hides them", async () => {
    const { user } = await loaded();

    await user.click(within(row("Quarta-feira")).getByRole("checkbox", { name: "Fechado" }));
    expect(opens("Quarta-feira")).toHaveValue("09:00");
    expect(closes("Quarta-feira")).toHaveValue("22:00");

    await user.click(within(row("Quarta-feira")).getByRole("checkbox", { name: "Fechado" }));
    expect(within(row("Quarta-feira")).queryByLabelText(/abertura/)).not.toBeInTheDocument();
  });

  it("sends the whole week: closed days as closed, open days with their hours, and the slot length as a number", async () => {
    let body: PitchSchedule | undefined;
    server.use(
      http.put(url, async ({ request }) => {
        body = (await request.json()) as PitchSchedule;
        return HttpResponse.json(body as PitchSchedule);
      }),
    );
    const { user } = await loaded();
    await user.selectOptions(screen.getByLabelText("Duração do horário"), "90");
    await user.click(within(row("Segunda-feira")).getByRole("checkbox", { name: "Fechado" }));
    await setTime(user, closes("Segunda-feira"), "23:30");
    await user.click(within(row("Domingo")).getByRole("checkbox", { name: "Fechado" }));
    await setTime(user, opens("Domingo"), "10:00");
    await setTime(user, closes("Domingo"), "00:00");

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    expect(await screen.findByText("Horário guardado.")).toBeInTheDocument();
    expect(body).toEqual({
      slotDurationMinutes: 90,
      monday: open("09:00", "23:30"),
      tuesday: closed,
      wednesday: closed,
      thursday: closed,
      friday: closed,
      saturday: closed,
      sunday: open("10:00", "00:00"),
    });
  });

  it("accepts 00:00 as closing time (midnight)", async () => {
    let calls = 0;
    server.use(
      http.put(url, async ({ request }) => {
        calls++;
        return HttpResponse.json((await request.json()) as PitchSchedule);
      }),
    );
    const { user } = await loaded(schedule({ monday: open("22:00", "23:00") }));
    await setTime(user, closes("Segunda-feira"), "00:00");

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    await waitFor(() => expect(calls).toBe(1));
  });

  it("rejects closing before or at opening, on the closing field of that row only, without calling the API", async () => {
    let calls = 0;
    server.use(http.put(url, () => (calls++, HttpResponse.json(schedule()))));
    const { user } = await loaded(schedule({ monday: open("10:00", "09:00"), tuesday: open("09:00", "09:00"), wednesday: open("09:00", "10:00") }));

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    const message =
      "A hora de fecho tem de ser depois da abertura. O fecho mais tarde é 00:00 (meia-noite); horários que passam da meia-noite não são aceites.";
    expect(closes("Segunda-feira")).toHaveAccessibleDescription(message);
    expect(closes("Terça-feira")).toHaveAccessibleDescription(message);
    expect(closes("Quarta-feira")).not.toHaveAttribute("aria-invalid");
    expect(opens("Segunda-feira")).not.toHaveAttribute("aria-invalid");
    expect(calls).toBe(0);
  });

  it("rejects hours past midnight such as 22:00 to 02:00", async () => {
    const { user } = await loaded(schedule({ friday: open("22:00", "02:00") }));

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    expect(closes("Sexta-feira")).toHaveAccessibleDescription(expect.stringContaining("horários que passam da meia-noite não são aceites"));
  });

  it("asks for both times of an open day and for times in 30-minute steps", async () => {
    const { user } = await loaded(schedule({ monday: open("09:00", "22:00"), tuesday: open("09:00", "22:00") }));
    await user.clear(opens("Segunda-feira"));
    await setTime(user, closes("Terça-feira"), "21:45");

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    expect(opens("Segunda-feira")).toHaveAccessibleDescription("Indica a hora de abertura.");
    expect(closes("Terça-feira")).toHaveAccessibleDescription("As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.");
  });

  it("does not check the times of a closed day", async () => {
    let calls = 0;
    server.use(http.put(url, () => (calls++, HttpResponse.json(schedule()))));
    const { user } = await loaded(schedule({ monday: open("09:00", "22:00") }));
    await user.click(within(row("Segunda-feira")).getByRole("checkbox", { name: "Fechado" }));

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    await waitFor(() => expect(calls).toBe(1));
  });

  it("puts API errors on the right row and field", async () => {
    server.use(
      http.put(url, () =>
        problem(400, "validation_failed", {
          slotDurationMinutes: ["A duração do horário tem de ser 30, 60 ou 90 minutos."],
          "tuesday.opens": ["As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30."],
          "thursday.closes": ["A hora de fecho tem de ser depois da abertura."],
          friday: ["Um dia fechado não tem horas: tira as horas de sexta-feira ou abre o dia."],
        }),
      ),
    );
    const { user } = await loaded(schedule({ tuesday: open("09:00", "12:00"), thursday: open("09:00", "12:00") }));

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    await waitFor(() => expect(screen.getByLabelText("Duração do horário")).toHaveAccessibleDescription("A duração do horário tem de ser 30, 60 ou 90 minutos."));
    expect(opens("Terça-feira")).toHaveAccessibleDescription("As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.");
    expect(closes("Terça-feira")).not.toHaveAttribute("aria-invalid");
    expect(closes("Quinta-feira")).toHaveAccessibleDescription("A hora de fecho tem de ser depois da abertura.");
    expect(opens("Quinta-feira")).not.toHaveAttribute("aria-invalid");
    expect(within(row("Sexta-feira")).getByText("Um dia fechado não tem horas: tira as horas de sexta-feira ou abre o dia.")).toBeInTheDocument();
    expect(within(row("Segunda-feira")).queryByText(/Um dia fechado/)).not.toBeInTheDocument();
  });

  it("tells the owner when the pitch is gone (pitch_not_found) and closes the editor", async () => {
    server.use(http.put(url, () => problem(404, "pitch_not_found")));
    const { user } = await loaded();

    await user.click(screen.getByRole("button", { name: "Guardar horário" }));

    expect((await screen.findAllByText("Este campo já não existe neste clube.")).length).toBeGreaterThan(0);
    await waitFor(() => expect(onGone).toHaveBeenCalled());
  });

  it("closes the editor when the pitch to load is not found", async () => {
    server.use(http.get(url, () => problem(404, "pitch_not_found")));

    renderWithProviders(<ScheduleEditor clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    await waitFor(() => expect(onGone).toHaveBeenCalled());
  });

  it("shows an error with a way to try again when the schedule cannot be loaded", async () => {
    let calls = 0;
    server.use(http.get(url, () => (++calls === 1 ? problem(500, "internal_error") : HttpResponse.json(schedule()))));
    renderWithProviders(<ScheduleEditor clubId={clubId} pitchId={pitchId} onGone={onGone} />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
    await userEvent.setup().click(screen.getByRole("button", { name: "Tentar novamente" }));

    expect(await screen.findByRole("form", { name: "Horário do campo" })).toBeInTheDocument();
  });
});
