import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Account } from "@/features/auth/api";
import { LoginForm } from "@/features/auth/components/login-form";
import type { PublicClub } from "@/features/catalog/types";
import { currentPath, replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ClubBooking } from "./club-booking";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const padel = "0199f7a0-0000-7000-8000-0000000000d1";
const BASE = "/clubs/clube-da-foz";
const FIRST = "2026-05-04T09:00:00Z"; // Monday 10:00 in Lisbon (WEST)
const URL_WITH_SLOT = `${BASE}?pitch=${padel}&date=2026-05-04&slot=${encodeURIComponent(FIRST)}`;

const club: PublicClub = {
  slug: "clube-da-foz",
  name: "Clube da Foz",
  address: "Rua do Mar 1",
  locality: "Porto",
  phone: "912345678",
  contactEmail: "geral@foz.pt",
  pitches: [{ id: padel, name: "Padel 1", sport: "padel", pricePerSlot: 18.5, hasPriceBands: true }],
};

const account = (profile: Account["profile"]): Account => ({ id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rita Jogadora", email: "rita@exemplo.pt", profile });

const hour = (startsAt: string) => ({ startsAt, endsAt: new Date(new Date(startsAt).getTime() + 3_600_000).toISOString().replace(".000Z", "Z") });

const occurrence = (startsAt: string, price: number, reason: string | null = null) => ({
  ...hour(startsAt),
  price,
  status: reason === null ? "free" : "unavailable",
  reason,
});

const booking = (n: number, startsAt: string, price: number) => ({
  id: `0199f7a0-0000-7000-8000-${String(n).padStart(12, "0")}`,
  pitchId: padel,
  clubId: "0199f7a0-0000-7000-8000-0000000000c1",
  ...hour(startsAt),
  price,
  status: "confirmed",
});

function problem(status: number, code: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json({ status, title: "Erro", code, traceId: "t", ...extra }, { status, headers: { "Content-Type": "application/problem+json" } });
}

/** Four weeks of Mondays: the 2nd (18 May) is taken and the 3rd (25 May) is in a 30 € price band. */
const preview = {
  weeks: 4,
  occurrences: [
    occurrence("2026-05-04T09:00:00Z", 18.5),
    occurrence("2026-05-11T09:00:00Z", 18.5, "taken"),
    occurrence("2026-05-18T09:00:00Z", 30),
    occurrence("2026-05-25T09:00:00Z", 30),
  ],
};

type World = {
  me: Account | null;
  previewAnswer?: () => Response;
  createAnswer?: (body: { pitchId: string; startsAt: string; weeks: number }) => Response;
};

/** The API: the session, one day of slots, the series preview and the series booking; returns what was asked. */
function serve(world: World) {
  const previews: { pitchId: string; startsAt: string; weeks: number }[] = [];
  const creates: { pitchId: string; startsAt: string; weeks: number }[] = [];
  server.use(
    http.get("/api/auth/me", () => (world.me ? HttpResponse.json(world.me) : problem(401, "unauthorized"))),
    http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
    http.get("/api/clubs/clube-da-foz/pitches/:pitchId/slots", () =>
      HttpResponse.json({ date: "2026-05-04", slots: [{ ...hour(FIRST), price: 18.5, available: true }, { ...hour("2026-05-04T10:00:00Z"), price: 30, available: true }] }),
    ),
    http.post("/api/bookings/recurring/preview", async ({ request }) => {
      previews.push((await request.json()) as (typeof previews)[number]);
      return world.previewAnswer?.() ?? HttpResponse.json(preview);
    }),
    http.post("/api/bookings/recurring", async ({ request }) => {
      const body = (await request.json()) as (typeof creates)[number];
      creates.push(body);
      return world.createAnswer?.(body) ?? HttpResponse.json({ seriesId: "0199f7a0-0000-7000-8000-0000000000e1", created: [], skipped: [] }, { status: 201 });
    }),
  );
  return { previews, creates };
}

const summary = () => screen.findByRole("region", { name: "Resumo da reserva" });
const text = (element: HTMLElement) => (element.textContent ?? "").replace(/\s/g, " ");

describe("weekly bookings on the club page (US20)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-01T10:00:00Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("offers 1 to 4 weeks, writes the choice in the address and takes it out again for 1", async () => {
    resetNavigation(URL_WITH_SLOT);
    serve({ me: account("Player") });
    renderWithProviders(<ClubBooking club={club} />);
    await summary();

    const select = screen.getByLabelText("Repetir semanalmente");
    expect(within(select).getAllByRole("option").map((option) => option.textContent)).toEqual(["1 semana (sem repetição)", "2 semanas", "3 semanas", "4 semanas"]);
    expect(select).toHaveValue("1");
    expect(screen.queryByRole("list", { name: "Datas da reserva semanal" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirmar reserva" })).toBeEnabled();

    await userEvent.selectOptions(select, "3");
    await waitFor(() => expect(currentPath()).toBe(`${URL_WITH_SLOT}&weeks=3`));
    await userEvent.selectOptions(screen.getByLabelText("Repetir semanalmente"), "1");

    await waitFor(() => expect(currentPath()).toBe(URL_WITH_SLOT));
  });

  it("takes a weeks value outside 1 to 4 as no repetition", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=9`);
    const { previews } = serve({ me: account("Player") });
    renderWithProviders(<ClubBooking club={club} />);
    await summary();

    expect(screen.getByLabelText("Repetir semanalmente")).toHaveValue("1");
    expect(screen.getByRole("button", { name: "Confirmar reserva" })).toBeEnabled();
    expect(previews).toEqual([]);
  });

  it("asks which dates are free and shows each with its price, free or unavailable and why", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=4`);
    const { previews } = serve({ me: account("Player") });
    renderWithProviders(<ClubBooking club={club} />);

    const list = await screen.findByRole("list", { name: "Datas da reserva semanal" });

    expect(previews).toEqual([{ pitchId: padel, startsAt: FIRST, weeks: 4 }]);
    const items = within(list).getAllByRole("listitem");
    expect(items.map(text)).toEqual([
      "segunda-feira, 4 de maio, 10:00 – 11:00 18,50 € · Livre",
      "segunda-feira, 11 de maio, 10:00 – 11:00 18,50 € · Indisponível: já está reservado",
      "segunda-feira, 18 de maio, 10:00 – 11:00 30,00 € · Livre",
      "segunda-feira, 25 de maio, 10:00 – 11:00 30,00 € · Livre",
    ]);
    expect(items.map((item) => item.getAttribute("data-status"))).toEqual(["free", "unavailable", "free", "free"]);
    expect(screen.getByText("3 de 4 datas estão livres; as outras ficam de fora.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirmar 3 reservas" })).toBeEnabled();
    // A single booking is not on offer in the same place.
    expect(screen.queryByRole("button", { name: "Confirmar reserva" })).not.toBeInTheDocument();
  });

  it("says every reason in words, and keeps the button off when no date is free", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=3`);
    serve({
      me: account("Player"),
      previewAnswer: () =>
        HttpResponse.json({
          weeks: 3,
          occurrences: [occurrence("2026-05-04T09:00:00Z", 18.5, "blocked"), occurrence("2026-05-11T09:00:00Z", 18.5, "closed"), occurrence("2026-05-18T09:00:00Z", 18.5, "outside_window")],
        }),
    });
    renderWithProviders(<ClubBooking club={club} />);

    const list = await screen.findByRole("list", { name: "Datas da reserva semanal" });

    expect(within(list).getByText("Indisponível: está bloqueado pelo clube")).toBeInTheDocument();
    expect(within(list).getByText("Indisponível: o campo está fechado nesse dia")).toBeInTheDocument();
    expect(within(list).getByText("Indisponível: fica fora do período de reservas (até 30 dias)")).toBeInTheDocument();
    expect(screen.getByText("Nenhuma das datas está livre.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirmar reservas" })).toBeDisabled();
  });

  it("books the series and shows what was booked and what was left out, with the way to the player's bookings", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=4`);
    const { creates } = serve({
      me: account("Player"),
      createAnswer: () =>
        HttpResponse.json(
          {
            seriesId: "0199f7a0-0000-7000-8000-0000000000e1",
            created: [booking(1, "2026-05-04T09:00:00Z", 18.5), booking(2, "2026-05-18T09:00:00Z", 30), booking(3, "2026-05-25T09:00:00Z", 30)],
            skipped: [{ ...hour("2026-05-11T09:00:00Z"), reason: "taken" }],
          },
          { status: 201 },
        ),
    });
    renderWithProviders(<ClubBooking club={club} />);
    await userEvent.click(await screen.findByRole("button", { name: "Confirmar 3 reservas" }));

    const result = await screen.findByRole("status", { name: "3 reservas confirmadas" });

    expect(creates).toEqual([{ pitchId: padel, startsAt: FIRST, weeks: 4 }]);
    expect(within(result).getAllByRole("listitem").map(text)).toEqual([
      "segunda-feira, 4 de maio, 10:00 – 11:00 · 18,50 €",
      "segunda-feira, 18 de maio, 10:00 – 11:00 · 30,00 €",
      "segunda-feira, 25 de maio, 10:00 – 11:00 · 30,00 €",
      "segunda-feira, 11 de maio, 10:00 – 11:00: já está reservado",
    ]);
    expect(within(result).getByText("Ficaram de fora:")).toBeInTheDocument();
    expect(within(result).getByRole("link", { name: "Ver as minhas reservas" })).toHaveAttribute("href", "/bookings");
    // The choice of slot is cleared, like after a single booking; the weeks stay in the address.
    expect(currentPath()).not.toContain("slot=");
  });

  it("does not claim anything was skipped when every date was booked", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=2`);
    serve({
      me: account("Player"),
      previewAnswer: () => HttpResponse.json({ weeks: 2, occurrences: [occurrence("2026-05-04T09:00:00Z", 18.5), occurrence("2026-05-11T09:00:00Z", 18.5)] }),
      createAnswer: () =>
        HttpResponse.json({ seriesId: "0199f7a0-0000-7000-8000-0000000000e1", created: [booking(1, "2026-05-04T09:00:00Z", 18.5), booking(2, "2026-05-11T09:00:00Z", 18.5)], skipped: [] }, { status: 201 }),
    });
    renderWithProviders(<ClubBooking club={club} />);
    await userEvent.click(await screen.findByRole("button", { name: "Confirmar 2 reservas" }));

    const result = await screen.findByRole("status", { name: "2 reservas confirmadas" });

    expect(within(result).queryByText("Ficaram de fora:")).not.toBeInTheDocument();
  });

  it("shows the message and the dates when none could be booked (409), and the page works again", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=2`);
    serve({
      me: account("Player"),
      previewAnswer: () => HttpResponse.json({ weeks: 2, occurrences: [occurrence("2026-05-04T09:00:00Z", 18.5), occurrence("2026-05-11T09:00:00Z", 18.5)] }),
      createAnswer: () =>
        problem(409, "series_unavailable", { skipped: [{ ...hour("2026-05-04T09:00:00Z"), reason: "taken" }, { ...hour("2026-05-11T09:00:00Z"), reason: "blocked" }] }),
    });
    renderWithProviders(<ClubBooking club={club} />);
    await userEvent.click(await screen.findByRole("button", { name: "Confirmar 2 reservas" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Nenhuma das datas escolhidas está disponível, por isso não foi feita nenhuma reserva.");
    const dates = await screen.findByRole("list", { name: "Datas que ficaram de fora" });
    expect(within(dates).getAllByRole("listitem").map(text)).toEqual([
      "segunda-feira, 4 de maio, 10:00 – 11:00: já está reservado",
      "segunda-feira, 11 de maio, 10:00 – 11:00: está bloqueado pelo clube",
    ]);
    expect(screen.queryByRole("status", { name: /reservas? confirmadas?/ })).not.toBeInTheDocument();
  });

  it("sends the player to sign in again, coming back to the same slot and weeks, when the session ended", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=2`);
    serve({
      me: account("Player"),
      previewAnswer: () => HttpResponse.json({ weeks: 2, occurrences: [occurrence("2026-05-04T09:00:00Z", 18.5), occurrence("2026-05-11T09:00:00Z", 18.5)] }),
      createAnswer: () => problem(401, "unauthorized"),
    });
    renderWithProviders(<ClubBooking club={club} />);
    await userEvent.click(await screen.findByRole("button", { name: "Confirmar 2 reservas" }));

    await waitFor(() => expect(replace).not.toHaveBeenCalledWith(expect.stringContaining("/login"), expect.anything()));
    // The router push goes to the login page with this address as `next`; read it back from the mock's path.
    await waitFor(() => expect(currentPath()).toContain("/login?next="));
    expect(decodeURIComponent(currentPath())).toContain(`weeks=2`);
  });

  it("lets a visitor choose the weeks without asking the API for the dates, and the sign-in link keeps them", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=3`);
    const { previews } = serve({ me: null });
    renderWithProviders(<ClubBooking club={club} />);
    await summary();

    expect(screen.getByLabelText("Repetir semanalmente")).toHaveValue("3");
    const link = await screen.findByRole("link", { name: "Entrar para reservar" });
    const next = new URL(link.getAttribute("href") ?? "", "http://localhost").searchParams.get("next") ?? "";
    expect(next).toBe(`${BASE}?pitch=${padel}&date=2026-05-04&slot=${encodeURIComponent(FIRST)}&weeks=3`);
    expect(previews).toEqual([]);
    expect(screen.queryByRole("list", { name: "Datas da reserva semanal" })).not.toBeInTheDocument();
  });

  it("brings the player back after signing in with the same slot and weeks, and the dates are shown", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=3`);
    const world: World = { me: null };
    const { previews } = serve(world);
    const visitor = renderWithProviders(<ClubBooking club={club} />);
    const link = await screen.findByRole("link", { name: "Entrar para reservar" });
    const next = new URL(link.getAttribute("href") ?? "", "http://localhost").searchParams.get("next") ?? "";
    visitor.unmount();

    // Sign in on the login page, which was given where to go back to.
    server.use(
      http.post("/api/auth/login", () => {
        world.me = account("Player");
        return HttpResponse.json(account("Player"));
      }),
    );
    resetNavigation("/login");
    const login = renderWithProviders(<LoginForm next={next} />);
    await userEvent.type(screen.getByLabelText(/^email$/i), "rita@exemplo.pt");
    await userEvent.type(screen.getByLabelText(/palavra-passe/i), "palavra-passe");
    await userEvent.click(screen.getByRole("button", { name: "Entrar" }));
    await waitFor(() => expect(currentPath()).toBe(next));
    login.unmount();

    renderWithProviders(<ClubBooking club={club} />);
    await screen.findByRole("list", { name: "Datas da reserva semanal" });

    expect(screen.getByLabelText("Repetir semanalmente")).toHaveValue("3");
    expect(previews).toEqual([{ pitchId: padel, startsAt: FIRST, weeks: 3 }]);
  });

  it("does not show the weeks to an owner, who cannot book", async () => {
    resetNavigation(`${URL_WITH_SLOT}&weeks=3`);
    serve({ me: account("Owner") });
    renderWithProviders(<ClubBooking club={club} />);

    await screen.findByRole("list", { name: "Horários" });
    expect(screen.queryByLabelText("Repetir semanalmente")).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Resumo da reserva" })).not.toBeInTheDocument();
  });
});
