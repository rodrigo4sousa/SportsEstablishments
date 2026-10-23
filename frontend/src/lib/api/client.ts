import type { components } from "./schema";

/**
 * Browser client for the API on the same origin (`/api/*`, rewritten to ASP.NET Core).
 * The session lives in httpOnly cookies, so this code never sees a token.
 */

type ValidationProblemDetails = components["schemas"]["ValidationProblemDetails"];

/** Code used when the request never got a response (offline, DNS, server down). */
export const NETWORK_ERROR = "network_error";

/** An error response from the API, read from its ProblemDetails. Show messages by `code`, never by text. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly fieldErrors: Readonly<Record<string, string[]>> = {},
    readonly traceId?: string,
  ) {
    super(`API request failed: ${status} ${code}`);
    this.name = "ApiError";
  }
}

// Requests that must not trigger a refresh on 401: the refresh itself and those that create a session.
const SESSION_ENDPOINTS = new Set(["/api/auth/refresh", "/api/auth/register", "/api/auth/login"]);

let refreshInFlight: Promise<boolean> | null = null;

/** Renews the session once; concurrent callers share the same refresh (docs/stack.md, step 3). */
function refreshSession(): Promise<boolean> {
  refreshInFlight ??= fetch("/api/auth/refresh", { method: "POST", credentials: "same-origin" })
    .then((response) => response.ok)
    .catch(() => false)
    .finally(() => {
      refreshInFlight = null;
    });
  return refreshInFlight;
}

async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(path, { ...init, credentials: "same-origin" });
  } catch {
    throw new ApiError(0, NETWORK_ERROR);
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const problem = (await response.json().catch(() => null)) as Partial<ValidationProblemDetails> | null;
  return new ApiError(
    response.status,
    typeof problem?.code === "string" ? problem.code : "unknown_error",
    (problem?.errors as Record<string, string[]> | undefined) ?? {},
    problem?.traceId,
  );
}

/**
 * Sends a request; on a 401 it refreshes the session once and repeats the request.
 * Throws {@link ApiError} for any error status.
 */
export async function apiRequest(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body !== undefined && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  const request = { ...init, headers };

  let response = await send(path, request);
  if (response.status === 401 && !SESSION_ENDPOINTS.has(path) && (await refreshSession())) {
    response = await send(path, request);
  }
  if (!response.ok) {
    throw await toApiError(response);
  }
  return response;
}

/** {@link apiRequest} for a JSON response body. */
export async function apiJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await apiRequest(path, init);
  return (await response.json()) as T;
}
