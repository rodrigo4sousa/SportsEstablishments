import { ApiError, apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Account = components["schemas"]["AccountResponse"];
export type RegisterPlayerRequest = components["schemas"]["RegisterPlayerRequest"];

export const currentUserQueryKey = ["auth", "me"] as const;

/** The signed-in account, or null when there is no session (even after the refresh attempt). */
export async function fetchCurrentUser(): Promise<Account | null> {
  try {
    return await apiJson<Account>("/api/auth/me");
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }
    throw error;
  }
}

/** US01: creates the player account; the API answers with the session cookies already set. */
export function registerPlayer(request: RegisterPlayerRequest): Promise<Account> {
  return apiJson<Account>("/api/auth/register", { method: "POST", body: JSON.stringify(request) });
}
