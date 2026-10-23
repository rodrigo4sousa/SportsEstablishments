import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { clubQueryKey, type OwnerClub } from "../api";
import { ClubForm } from "./club-form";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));

const saved: OwnerClub = {
  id: "0199f7a0-0000-7000-8000-0000000000c1",
  slug: "clube-de-tenis-da-foz",
  name: "Clube de Ténis da Foz",
  address: "Rua do Mar, 1",
  locality: "Porto",
  phone: "912345678",
  contactEmail: "geral@tenisfoz.pt",
  cancellationWindowHours: 24,
  timeZone: "Europe/Lisbon",
};

function problem(status: number, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...(errors ? { errors } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

const field = (name: RegExp) => screen.getByLabelText(name);

async function fillIn(values: Partial<Record<"name" | "address" | "locality" | "phone" | "email" | "window", string>>) {
  const user = userEvent.setup();
  const type = async (label: RegExp, value?: string) => {
    if (value === undefined) return;
    await user.clear(field(label));
    if (value !== "") await user.type(field(label), value);
  };
  await type(/^nome$/i, values.name);
  await type(/^morada$/i, values.address);
  await type(/^localidade$/i, values.locality);
  await type(/^telefone$/i, values.phone);
  await type(/email de contacto/i, values.email);
  await type(/janela de cancelamento/i, values.window);
  return user;
}

const complete = { name: "Clube de Ténis da Foz", address: "Rua do Mar, 1", locality: "Porto", phone: "912 345 678", email: "geral@tenisfoz.pt" };

function expectFieldError(name: RegExp, message: string) {
  expect(field(name)).toHaveAttribute("aria-invalid", "true");
  expect(field(name)).toHaveAccessibleDescription(expect.stringContaining(message));
}

describe("ClubForm (new club)", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  it("starts with the default cancellation window and the other fields empty", () => {
    renderWithProviders(<ClubForm />);

    expect(field(/janela de cancelamento/i)).toHaveValue("24");
    expect(field(/^nome$/i)).toHaveValue("");
    expect(screen.getByRole("button", { name: "Criar clube" })).toBeInTheDocument();
    expect(screen.queryByText(/endereço público/i)).not.toBeInTheDocument();
  });

  it("shows one message under each invalid field and does not call the API", async () => {
    let requests = 0;
    server.use(
      http.post("/api/manage/clubs", () => {
        requests++;
        return HttpResponse.json(saved, { status: 201 });
      }),
    );
    renderWithProviders(<ClubForm />);

    const user = await fillIn({ phone: "123", email: "geral@clube", window: "0" });
    await user.click(screen.getByRole("button", { name: "Criar clube" }));

    expectFieldError(/^nome$/i, "Indica o nome do clube.");
    expectFieldError(/^morada$/i, "Indica a morada do clube.");
    expectFieldError(/^localidade$/i, "Indica a localidade do clube.");
    expectFieldError(/^telefone$/i, "Indica um número de telefone válido, com 9 a 15 algarismos.");
    expectFieldError(/email de contacto/i, "Indica um email válido, por exemplo nome@exemplo.pt.");
    expectFieldError(/janela de cancelamento/i, "A janela de cancelamento tem de estar entre 1 e 168 horas.");
    expect(field(/^nome$/i)).toHaveFocus();
    expect(requests).toBe(0);
  });

  it("creates the club with normalized values and goes to its page", async () => {
    let body: unknown;
    server.use(
      http.post("/api/manage/clubs", async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(saved, { status: 201 });
      }),
    );
    const { queryClient } = renderWithProviders(<ClubForm />);

    const user = await fillIn({ ...complete, name: "  Clube de Ténis da Foz ", window: "48" });
    await user.click(screen.getByRole("button", { name: "Criar clube" }));

    await waitFor(() => expect(replace).toHaveBeenCalledWith(`/manage/clubs/${saved.id}`));
    expect(body).toEqual({
      name: "Clube de Ténis da Foz",
      address: "Rua do Mar, 1",
      locality: "Porto",
      phone: "912345678",
      contactEmail: "geral@tenisfoz.pt",
      cancellationWindowHours: 48,
    });
    expect(queryClient.getQueryData(clubQueryKey(saved.id))).toEqual(saved);
    expect(await screen.findByText("Clube criado.")).toBeInTheDocument();
  });

  it("refreshes the lists of clubs after creating one", async () => {
    server.use(http.post("/api/manage/clubs", () => HttpResponse.json(saved, { status: 201 })));
    const { queryClient } = renderWithProviders(<ClubForm />);
    queryClient.setQueryData(["clubs", "list", 1], { items: [], page: 1, pageSize: 20, totalCount: 0 });

    const user = await fillIn(complete);
    await user.click(screen.getByRole("button", { name: "Criar clube" }));

    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(queryClient.getQueryState(["clubs", "list", 1])?.isInvalidated).toBe(true);
  });

  it("puts the API's validation messages on their fields", async () => {
    server.use(
      http.post("/api/manage/clubs", () =>
        problem(400, "validation_failed", {
          locality: ["Indica a localidade do clube."],
          cancellationWindowHours: ["A janela de cancelamento tem de estar entre 1 e 168 horas."],
        }),
      ),
    );
    renderWithProviders(<ClubForm />);

    const user = await fillIn(complete);
    await user.click(screen.getByRole("button", { name: "Criar clube" }));

    await waitFor(() => expectFieldError(/^localidade$/i, "Indica a localidade do clube."));
    expectFieldError(/janela de cancelamento/i, "A janela de cancelamento tem de estar entre 1 e 168 horas.");
    expect(field(/^nome$/i)).not.toHaveAttribute("aria-invalid");
    expect(field(/^localidade$/i)).toHaveFocus();
    expect(replace).not.toHaveBeenCalled();
  });

  it.each([
    [500, "internal_error", "Ocorreu um erro inesperado. Tenta novamente mais tarde."],
    [429, "too_many_requests", "Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente."],
    [403, "origin_not_allowed", "O pedido foi recusado. Recarrega a página e tenta novamente."],
  ])("shows a toast for a %i %s and keeps what was typed", async (status, code, message) => {
    server.use(http.post("/api/manage/clubs", () => problem(status, code)));
    renderWithProviders(<ClubForm />);

    const user = await fillIn(complete);
    await user.click(screen.getByRole("button", { name: "Criar clube" }));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(field(/^nome$/i)).toHaveValue("Clube de Ténis da Foz");
    expect(replace).not.toHaveBeenCalled();
  });

  it("disables the button while the club is being created", async () => {
    let release: () => void = () => {};
    server.use(
      http.post("/api/manage/clubs", async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return HttpResponse.json(saved, { status: 201 });
      }),
    );
    renderWithProviders(<ClubForm />);

    const user = await fillIn(complete);
    await user.click(screen.getByRole("button", { name: "Criar clube" }));

    expect(await screen.findByRole("button", { name: "A guardar…" })).toBeDisabled();
    release();
    await waitFor(() => expect(replace).toHaveBeenCalled());
  });
});

describe("ClubForm (existing club)", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  it("shows the club's values and its public address, which does not change with the name", () => {
    renderWithProviders(<ClubForm club={saved} />);

    expect(field(/^nome$/i)).toHaveValue("Clube de Ténis da Foz");
    expect(field(/^telefone$/i)).toHaveValue("912345678");
    expect(field(/janela de cancelamento/i)).toHaveValue("24");
    expect(screen.getByText("/clubs/clube-de-tenis-da-foz")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Guardar alterações" })).toBeInTheDocument();
  });

  it("saves the changes with a PUT, updates the cache and stays on the page", async () => {
    let received: { url: string; body: unknown } | null = null;
    const updated = { ...saved, name: "Foz Ténis & Padel", cancellationWindowHours: 6 };
    server.use(
      http.put(`/api/manage/clubs/${saved.id}`, async ({ request }) => {
        received = { url: new URL(request.url).pathname, body: await request.json() };
        return HttpResponse.json(updated);
      }),
    );
    const { queryClient } = renderWithProviders(<ClubForm club={saved} />);

    const user = await fillIn({ name: "Foz Ténis & Padel", window: "6" });
    await user.click(screen.getByRole("button", { name: "Guardar alterações" }));

    expect(await screen.findByText("Alterações guardadas.")).toBeInTheDocument();
    expect(received).toEqual({
      url: `/api/manage/clubs/${saved.id}`,
      body: {
        name: "Foz Ténis & Padel",
        address: "Rua do Mar, 1",
        locality: "Porto",
        phone: "912345678",
        contactEmail: "geral@tenisfoz.pt",
        cancellationWindowHours: 6,
      },
    });
    expect(queryClient.getQueryData(clubQueryKey(saved.id))).toEqual(updated);
    expect(replace).not.toHaveBeenCalled();
    expect(field(/^nome$/i)).toHaveValue("Foz Ténis & Padel");
  });

  it("puts the API's validation messages on their fields when editing", async () => {
    server.use(
      http.put(`/api/manage/clubs/${saved.id}`, () =>
        problem(400, "validation_failed", { contactEmail: ["Indica um email válido, por exemplo nome@exemplo.pt."] }),
      ),
    );
    renderWithProviders(<ClubForm club={saved} />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Guardar alterações" }));

    await waitFor(() => expectFieldError(/email de contacto/i, "Indica um email válido, por exemplo nome@exemplo.pt."));
  });

  it("shows a toast, chosen by code, when the club is gone", async () => {
    server.use(http.put(`/api/manage/clubs/${saved.id}`, () => problem(404, "club_not_found")));
    renderWithProviders(<ClubForm club={saved} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Guardar alterações" }));

    expect(await screen.findByText("Este clube não existe ou não é teu.")).toBeInTheDocument();
  });
});
