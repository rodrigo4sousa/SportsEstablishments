import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { ApiError, NETWORK_ERROR, apiJson } from "./client";
import { testTranslator } from "@/test/intl";
import { apiErrorMessage } from "./error-messages";

const errorMessage = (code: string) => apiErrorMessage(testTranslator("apiErrors"), code);

function problem(status: number, code: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01", ...extra },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

async function caught(promise: Promise<unknown>): Promise<ApiError> {
  const error = await promise.then(
    () => null,
    (reason: unknown) => reason,
  );
  expect(error).toBeInstanceOf(ApiError);
  return error as ApiError;
}

describe("apiJson", () => {
  it("returns the JSON body of a successful response", async () => {
    server.use(http.get("/api/auth/me", () => HttpResponse.json({ name: "Ana" })));

    await expect(apiJson("/api/auth/me")).resolves.toEqual({ name: "Ana" });
  });

  it("sends JSON bodies with a JSON content type", async () => {
    let contentType: string | null = null;
    server.use(
      http.post("/api/auth/register", ({ request }) => {
        contentType = request.headers.get("Content-Type");
        return HttpResponse.json({}, { status: 201 });
      }),
    );

    await apiJson("/api/auth/register", { method: "POST", body: JSON.stringify({ name: "Ana" }) });

    expect(contentType).toBe("application/json");
  });

  it("turns ProblemDetails into an ApiError with code, field errors and trace id", async () => {
    server.use(
      http.post("/api/auth/register", () =>
        problem(400, "validation_failed", { errors: { email: ["Indica o teu email."] } }),
      ),
    );

    const error = await caught(apiJson("/api/auth/register", { method: "POST", body: "{}" }));

    expect(error.status).toBe(400);
    expect(error.code).toBe("validation_failed");
    expect(error.fieldErrors).toEqual({ email: ["Indica o teu email."] });
    expect(error.traceId).toBe("00-trace-01");
  });

  it("reports a request that never got a response as a network error", async () => {
    server.use(http.get("/api/auth/me", () => HttpResponse.error()));

    const error = await caught(apiJson("/api/auth/me"));

    expect(error.code).toBe(NETWORK_ERROR);
    expect(errorMessage(error.code)).toBe("Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.");
  });

  it("uses a generic code when the error body is not ProblemDetails", async () => {
    server.use(http.get("/api/auth/me", () => new HttpResponse("Bad gateway", { status: 502 })));

    const error = await caught(apiJson("/api/auth/me"));

    expect(error.status).toBe(502);
    expect(errorMessage(error.code)).toBe("Ocorreu um erro inesperado. Tenta novamente mais tarde.");
  });
});

describe("Retry-After on an error (UX-018)", () => {
  function tooMany(header: string | null) {
    server.use(
      http.get("/api/clubs", () =>
        HttpResponse.json(
          { status: 429, title: "Erro", code: "too_many_requests", traceId: "t" },
          { status: 429, headers: { "Content-Type": "application/problem+json", ...(header === null ? {} : { "Retry-After": header }) } },
        ),
      ),
    );
  }

  it("keeps the seconds the API asks to wait", async () => {
    tooMany("42");

    expect((await caught(apiJson("/api/clubs"))).retryAfterSeconds).toBe(42);
  });

  it.each([null, "0", "-5", "abc", "Wed, 21 Oct 2026 07:28:00 GMT", "1.5", "123456"])("ignores %s: the form then says only to wait a little", async (header) => {
    tooMany(header);

    expect((await caught(apiJson("/api/clubs"))).retryAfterSeconds).toBeUndefined();
  });
});

describe("401 handling", () => {
  it("refreshes the session once and repeats the request", async () => {
    let meCalls = 0;
    let refreshCalls = 0;
    server.use(
      http.get("/api/auth/me", () => (++meCalls === 1 ? problem(401, "unauthorized") : HttpResponse.json({ name: "Ana" }))),
      http.post("/api/auth/refresh", () => {
        refreshCalls++;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(apiJson("/api/auth/me")).resolves.toEqual({ name: "Ana" });
    expect(meCalls).toBe(2);
    expect(refreshCalls).toBe(1);
  });

  it("gives up with the 401 when the refresh fails, without retrying again", async () => {
    let meCalls = 0;
    server.use(
      http.get("/api/auth/me", () => {
        meCalls++;
        return problem(401, "unauthorized");
      }),
      http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
    );

    const error = await caught(apiJson("/api/auth/me"));

    expect(error.status).toBe(401);
    expect(error.code).toBe("unauthorized");
    expect(meCalls).toBe(1);
  });

  it("runs a single refresh for requests that fail at the same time", async () => {
    let refreshCalls = 0;
    const authorized = new Set<string>();
    server.use(
      http.get("/api/auth/me", ({ request }) => {
        const id = new URL(request.url).searchParams.get("n")!;
        if (authorized.has(id)) return HttpResponse.json({ id });
        authorized.add(id);
        return problem(401, "unauthorized");
      }),
      http.post("/api/auth/refresh", async () => {
        refreshCalls++;
        await new Promise((resolve) => setTimeout(resolve, 20));
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const results = await Promise.all(["1", "2", "3"].map((n) => apiJson(`/api/auth/me?n=${n}`)));

    expect(results).toEqual([{ id: "1" }, { id: "2" }, { id: "3" }]);
    expect(refreshCalls).toBe(1);
  });

  it("never refreshes for the endpoints that create a session", async () => {
    let refreshCalls = 0;
    server.use(
      http.post("/api/auth/register", () => problem(401, "unauthorized")),
      http.post("/api/auth/refresh", () => {
        refreshCalls++;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await caught(apiJson("/api/auth/register", { method: "POST", body: "{}" }));

    expect(refreshCalls).toBe(0);
  });
});
