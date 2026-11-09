import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Account } from "@/features/auth/api";
import { LoginForm } from "@/features/auth/components/login-form";
import { LoginScreen } from "@/features/auth/components/login-screen";
import { RegisterScreen } from "@/features/auth/components/register-screen";
import type { PublicClub } from "@/features/catalog/types";
import { currentPath, replace, resetNavigation } from "@/test/navigation";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { ClubBooking } from "./club-booking";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const padel = "0199f7a0-0000-7000-8000-0000000000d1";
const BASE = "/clubs/clube-da-foz";

const club: PublicClub = {
  slug: "clube-da-foz",
  name: "Clube da Foz",
  address: "Rua do Mar 1",
  locality: "Porto",
  phone: "912345678",
  contactEmail: "geral@foz.pt",
  pitches: [{ id: padel, name: "Padel 1", sport: "padel", pricePerSlot: 18.5, hasPriceBands: false }],
};

const slots = [
  { startsAt: "2026-05-04T09:00:00Z", endsAt: "2026-05-04T10:00:00Z", price: 18.5, available: true },
  { startsAt: "2026-05-04T10:00:00Z", endsAt: "2026-05-04T11:00:00Z", price: 18.5, available: true },
];

const account = (profile: Account["profile"]): Account => ({ id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rita", email: "rita@exemplo.pt", profile });

/** The API for a visitor who signs in as `profile` half way through. */
function serve(profile: Account["profile"]) {
  let signedIn = false;
  server.use(
    http.get("/api/auth/me", () => (signedIn ? HttpResponse.json(account(profile)) : HttpResponse.json({ status: 401, code: "unauthorized", traceId: "t" }, { status: 401 }))),
    http.post("/api/auth/refresh", () => HttpResponse.json({ status: 401, code: "invalid_refresh_token", traceId: "t" }, { status: 401 })),
    http.post("/api/auth/login", () => {
      signedIn = true;
      return HttpResponse.json(account(profile));
    }),
    http.post("/api/auth/register", () => {
      signedIn = true;
      return HttpResponse.json(account(profile), { status: 201 });
    }),
    http.get("/api/clubs/clube-da-foz/pitches/:pitchId/slots", () => HttpResponse.json({ date: "2026-05-04", slots })),
  );
}

async function pickSlotAsVisitor(): Promise<string> {
  renderWithProviders(<ClubBooking club={club} />);
  await screen.findByRole("list", { name: "Horários" });
  await userEvent.click(screen.getByRole("button", { name: /^11:00/ }));
  const link = await screen.findByRole("link", { name: "Entrar para reservar" });
  const next = new URL(link.getAttribute("href") ?? "", "http://localhost").searchParams.get("next");
  expect(next).not.toBeNull();
  return next ?? "";
}

async function signIn() {
  await userEvent.type(screen.getByLabelText(/^email$/i), "rita@exemplo.pt");
  await userEvent.type(screen.getByLabelText(/palavra-passe/i), "palavra-passe");
  await userEvent.click(screen.getByRole("button", { name: "Entrar" }));
}

describe("choosing a slot without a session, signing in, and coming back to it", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-05-01T10:00:00Z"));
    resetNavigation(`${BASE}?date=2026-05-04`);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("brings a player back to the same club, pitch, date and slot, with the summary open", async () => {
    serve("Player");
    const next = await pickSlotAsVisitor();

    // The visitor is now on the sign-in page, which is given where to go back to.
    document.body.innerHTML = "";
    resetNavigation("/login");
    const { unmount } = renderWithProviders(<LoginForm next={next} />);
    await signIn();

    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(currentPath()).toBe(next);
    expect(next).toBe(`${BASE}?date=2026-05-04&pitch=${padel}&slot=2026-05-04T10%3A00%3A00Z`);
    unmount();

    // Back on the club page: same pitch, same date, the same slot chosen, and now the player can confirm it.
    renderWithProviders(<ClubBooking club={club} />);
    const summary = await screen.findByRole("region", { name: "Resumo da reserva" });
    expect(summary).toHaveTextContent("11:00 – 12:00");
    expect(screen.getByLabelText("Campo")).toHaveValue(padel);
    expect(screen.getByLabelText("Data")).toHaveValue("2026-05-04");
    expect(await screen.findByRole("button", { name: "Confirmar reserva" })).toBeEnabled();
  });

  it("brings a visitor who creates an account instead back to the same slot too", async () => {
    serve("Player");
    const next = await pickSlotAsVisitor();

    // On the login page the visitor chooses "Criar conta": the link keeps where they came from.
    document.body.innerHTML = "";
    resetNavigation(`/login?next=${encodeURIComponent(next)}`);
    const login = renderWithProviders(<LoginScreen />);
    const toSignUp = await screen.findByRole("link", { name: "Criar conta" });
    const registerHref = toSignUp.getAttribute("href") ?? "";
    expect(registerHref).toBe(`/register?next=${encodeURIComponent(next)}`);
    login.unmount();

    // The sign-up page reads it and the new account lands on the same club page, pitch, date and slot.
    resetNavigation(registerHref);
    const signUp = renderWithProviders(<RegisterScreen />);
    await userEvent.type(screen.getByLabelText(/^nome$/i), "Rita Jogadora");
    await userEvent.type(screen.getByLabelText(/^email$/i), "rita@exemplo.pt");
    await userEvent.type(screen.getByLabelText(/palavra-passe/i), "palavra-passe");
    await userEvent.click(screen.getByRole("checkbox", { name: /Termos e Condições/ }));
    await userEvent.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(currentPath()).toBe(next));
    signUp.unmount();
    renderWithProviders(<ClubBooking club={club} />);
    const summary = await screen.findByRole("region", { name: "Resumo da reserva" });
    expect(summary).toHaveTextContent("11:00 – 12:00");
    expect(await screen.findByRole("button", { name: "Confirmar reserva" })).toBeEnabled();
  });

  it("sends an owner to their own area instead of a page where they cannot book", async () => {
    serve("Owner");
    const next = await pickSlotAsVisitor();
    document.body.innerHTML = "";
    resetNavigation("/login");
    renderWithProviders(<LoginForm next={next} />);

    await signIn();

    await waitFor(() => expect(currentPath()).toBe("/manage"));
  });
});
