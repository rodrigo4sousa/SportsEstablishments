import { ApiError, apiJson, apiRequest } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Account = components["schemas"]["AccountResponse"];
export type LoginRequest = components["schemas"]["LoginRequest"];
export type RegisterAccountRequest = components["schemas"]["RegisterAccountRequest"];

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

/** US03: signs in any profile; the API answers with the account and the session cookies already set. */
export function login(request: LoginRequest): Promise<Account> {
  return apiJson<Account>("/api/auth/login", { method: "POST", body: JSON.stringify(request) });
}

/** US03: ends the session (the API revokes the refresh token and clears both cookies). */
export async function logout(): Promise<void> {
  await apiRequest("/api/auth/logout", { method: "POST" });
}

/** Who is signing up: each has its own endpoint, and the API fixes the profile (it is never part of the request). */
export type RegistrationVariant = "player" | "owner";

const REGISTER_PATHS: Record<RegistrationVariant, string> = {
  player: "/api/auth/register",
  owner: "/api/auth/register/owner",
};

/** US01 and US02: creates a player or owner account; the API answers with the session cookies already set. */
export function registerAccount(variant: RegistrationVariant, request: RegisterAccountRequest): Promise<Account> {
  return apiJson<Account>(REGISTER_PATHS[variant], { method: "POST", body: JSON.stringify(request) });
}
