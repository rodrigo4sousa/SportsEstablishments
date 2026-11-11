import { describe, expect, it } from "vitest";
import { ApiError, NETWORK_ERROR } from "./client";
import { testTranslator } from "@/test/intl";
import { apiErrorMessage, unexpectedErrorCode } from "./error-messages";
import { isExpectedApiError } from "./expected-error";

const unexpectedErrorMessage = (error: unknown) => apiErrorMessage(testTranslator("apiErrors"), unexpectedErrorCode(error));

describe("isExpectedApiError", () => {
  it.each([400, 401, 403, 404, 409, 422, 429])("treats a %i from the API as expected: the screen has words for its code", (status) => {
    expect(isExpectedApiError(new ApiError(status, "algum_codigo"))).toBe(true);
  });

  it.each([500, 502, 503])("treats a %i as unexpected, so it goes to a toast (UX-019)", (status) => {
    expect(isExpectedApiError(new ApiError(status, "internal_error"))).toBe(false);
  });

  it("treats a request that got no answer as unexpected by default", () => {
    expect(isExpectedApiError(new ApiError(0, NETWORK_ERROR))).toBe(false);
  });

  it("treats it as expected on a screen that words it itself", () => {
    expect(isExpectedApiError(new ApiError(0, NETWORK_ERROR), { handlesNetworkError: true })).toBe(true);
  });

  it("still treats a 500 as unexpected on that screen", () => {
    expect(isExpectedApiError(new ApiError(500, "internal_error"), { handlesNetworkError: true })).toBe(false);
  });

  it.each([new Error("boom"), "texto", null, undefined])("treats something that is not an ApiError as unexpected", (error) => {
    expect(isExpectedApiError(error)).toBe(false);
  });
});

describe("unexpectedErrorMessage", () => {
  it("says the server could not be reached when there was no answer", () => {
    expect(unexpectedErrorMessage(new ApiError(0, NETWORK_ERROR))).toBe("Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.");
  });

  it.each([new ApiError(500, "internal_error"), new Error("boom"), null])("gives the general message otherwise, never the error's own text", (error) => {
    expect(unexpectedErrorMessage(error)).toBe("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
  });
});

describe("API error messages (T09)", () => {
  it("are chosen by code in the page's language, never from the server's text", () => {
    expect(apiErrorMessage(testTranslator("apiErrors", "en"), "slot_taken")).toBe("This time has just been booked. Choose another one.");
    expect(apiErrorMessage(testTranslator("apiErrors"), "slot_taken")).toBe("Este horário acabou de ser reservado. Escolhe outro.");
  });

  it("fall back to the general message for a code the catalogs do not have", () => {
    expect(apiErrorMessage(testTranslator("apiErrors", "en"), "something_new")).toBe("Something unexpected went wrong. Try again later.");
    expect(apiErrorMessage(testTranslator("apiErrors"), "bad_request")).toBe("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
  });
});
