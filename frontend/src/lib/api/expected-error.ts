import { ApiError, NETWORK_ERROR } from "./client";

type ExpectedErrorOptions = {
  /**
   * The screen words a request that got no answer (`network_error`) itself, next to what the user is doing (login, password
   * recovery, booking). Without this, no answer is unexpected, like any other failure the screen has no words for.
   */
  handlesNetworkError?: boolean;
};

/**
 * An answer the screen knows how to word: the API refused the request (a 4xx with a `code`). Anything else is unexpected and goes
 * to a toast (AGENTS.md): a 5xx, a request that got no answer (`ApiError` with status 0), or something that is not an
 * `ApiError`. Before this, only the last case reached the toast, and a 500 or a dropped connection ended up as an inline message
 * that could be out of view (UX-019).
 */
export function isExpectedApiError(error: unknown, { handlesNetworkError = false }: ExpectedErrorOptions = {}): error is ApiError {
  if (!(error instanceof ApiError)) {
    return false;
  }
  if (error.code === NETWORK_ERROR) {
    return handlesNetworkError;
  }
  return error.status >= 400 && error.status < 500;
}

// The toast text of an unexpected failure is `useApiErrors().unexpected(error)` (`error-messages.ts`), in the page's language.
