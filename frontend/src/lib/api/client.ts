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
const SESSION_ENDPOINTS = new Set(["/api/auth/refresh", "/api/auth/register", "/api/auth/login", "/api/auth/logout"]);

let refreshInFlight: Promise<boolean> | null = null;

/** Renews the session once; concurrent callers in this tab share the same refresh (docs/stack.md, step 3). */
function refreshSession(): Promise<boolean> {
  refreshInFlight ??= fetch("/api/auth/refresh", { method: "POST", credentials: "same-origin" })
    .then((response) => response.ok)
    .catch(() => false)
    .finally(() => {
      refreshInFlight = null;
    });
  return refreshInFlight;
}

/**
 * Name of the Web Lock that lets one tab at a time refresh. Tabs share the cookies, and a refresh token works once:
 * two tabs refreshing together would present the same token twice and end the session as a suspected theft.
 */
export const REFRESH_LOCK = "sportsbooking-refresh";

function tabLocks(): LockManager | null {
  return typeof navigator !== "undefined" && navigator.locks ? navigator.locks : null;
}

/**
 * Renews the session for a browser page that has no request to repeat (the login page's silent attempt).
 * Resolves to whether the session was renewed.
 */
export async function trySilentRefresh(): Promise<boolean> {
  const locks = tabLocks();
  return locks ? await locks.request(REFRESH_LOCK, refreshSession) : await refreshSession();
}

/**
 * After a 401: gets a response to the repeated request. With Web Locks, one tab at a time goes through here, and
 * the request is repeated first, since another tab may have refreshed while this one waited; only if it still gets
 * a 401 is the session refreshed. Without Web Locks, the session is refreshed and the request repeated.
 * A failed refresh returns the 401.
 */
async function retryAfterUnauthorized(path: string, request: RequestInit, unauthorized: Response): Promise<Response> {
  const locks = tabLocks();
  if (!locks) {
    return (await refreshSession()) ? send(path, request) : unauthorized;
  }

  return locks.request(REFRESH_LOCK, async () => {
    const repeated = await send(path, request);
    if (repeated.status !== 401) {
      return repeated;
    }
    return (await refreshSession()) ? send(path, request) : repeated;
  });
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
  if (response.status === 401 && !SESSION_ENDPOINTS.has(path)) {
    response = await retryAfterUnauthorized(path, request, response);
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
