import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import { currentUserQueryKey } from "../api";
import { LogoutButton } from "./logout-button";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn(), back: vi.fn(), forward: vi.fn(), prefetch: vi.fn() }),
}));

describe("LogoutButton", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  it("ends the session, clears everything cached and goes to the login page", async () => {
    let requests = 0;
    server.use(
      http.post("/api/auth/logout", () => {
        requests++;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { queryClient } = renderWithProviders(<LogoutButton />);
    queryClient.setQueryData(currentUserQueryKey, { name: "Ana" });
    queryClient.setQueryData(["clubs", "mine"], [{ id: 1 }]);

    await userEvent.setup().click(screen.getByRole("button", { name: "Sair" }));

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/login"));
    expect(requests).toBe(1);
    expect(queryClient.getQueryCache().getAll()).toHaveLength(0);
  });

  it("stays signed in, with a message, when the server cannot be reached", async () => {
    server.use(http.post("/api/auth/logout", () => HttpResponse.error()));
    const { queryClient } = renderWithProviders(<LogoutButton />);
    queryClient.setQueryData(currentUserQueryKey, { name: "Ana" });

    await userEvent.setup().click(screen.getByRole("button", { name: "Sair" }));

    expect(await screen.findByText("Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.")).toBeInTheDocument();
    expect(replace).not.toHaveBeenCalled();
    expect(queryClient.getQueryData(currentUserQueryKey)).toEqual({ name: "Ana" });
  });
});
