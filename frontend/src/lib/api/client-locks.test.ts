import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { ApiError, REFRESH_LOCK, apiJson, trySilentRefresh } from "./client";

function problem(status: number, code: string) {
  return HttpResponse.json(
    { status, title: "Erro", code, traceId: "00-trace-01" },
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

/** A minimal Web Locks API: one holder per name at a time, the rest wait in order (like several tabs of one browser). */
function installFakeLocks() {
  const queues = new Map<string, Promise<unknown>>();
  const log = { requested: [] as string[], concurrent: 0, maxConcurrent: 0 };
  const locks = {
    request: async (name: string, callback: () => unknown) => {
      log.requested.push(name);
      const previous = queues.get(name) ?? Promise.resolve();
      const run = previous.then(async () => {
        log.concurrent++;
        log.maxConcurrent = Math.max(log.maxConcurrent, log.concurrent);
        try {
          return await callback();
        } finally {
          log.concurrent--;
        }
      });
      queues.set(
        name,
        run.catch(() => undefined),
      );
      return run;
    },
  };
  Object.defineProperty(navigator, "locks", { configurable: true, value: locks });
  return log;
}

describe("refreshing across tabs (Web Locks)", () => {
  afterEach(() => {
    Reflect.deleteProperty(navigator, "locks");
  });

  it("works without the Web Locks API, as before", async () => {
    expect(navigator.locks).toBeUndefined();
    let refreshed = false;
    server.use(
      http.get("/api/auth/me", () => (refreshed ? HttpResponse.json({ name: "Ana" }) : problem(401, "unauthorized"))),
      http.post("/api/auth/refresh", () => {
        refreshed = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(apiJson("/api/auth/me")).resolves.toEqual({ name: "Ana" });
    await expect(trySilentRefresh()).resolves.toBe(true);
  });

  it("repeats the request first after getting the lock, and does not refresh if another tab already did", async () => {
    const log = installFakeLocks();
    let meCalls = 0;
    let refreshCalls = 0;
    server.use(
      // The first call fails; by the time the lock is ours, another tab has renewed the cookies.
      http.get("/api/auth/me", () => (++meCalls === 1 ? problem(401, "unauthorized") : HttpResponse.json({ name: "Ana" }))),
      http.post("/api/auth/refresh", () => {
        refreshCalls++;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(apiJson("/api/auth/me")).resolves.toEqual({ name: "Ana" });

    expect(log.requested).toEqual([REFRESH_LOCK]);
    expect(meCalls).toBe(2);
    expect(refreshCalls).toBe(0);
  });

  it("refreshes under the lock when the repeated request is still unauthorized, then repeats it once more", async () => {
    const log = installFakeLocks();
    let refreshed = false;
    let meCalls = 0;
    let refreshCalls = 0;
    server.use(
      http.get("/api/auth/me", () => {
        meCalls++;
        return refreshed ? HttpResponse.json({ name: "Ana" }) : problem(401, "unauthorized");
      }),
      http.post("/api/auth/refresh", () => {
        refreshCalls++;
        refreshed = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await expect(apiJson("/api/auth/me")).resolves.toEqual({ name: "Ana" });

    expect(log.requested).toEqual([REFRESH_LOCK]);
    expect(meCalls).toBe(3);
    expect(refreshCalls).toBe(1);
  });

  it("lets only one request at a time through, so requests failing together cause a single refresh", async () => {
    const log = installFakeLocks();
    let refreshed = false;
    let refreshCalls = 0;
    server.use(
      http.get("/api/auth/me", () => (refreshed ? HttpResponse.json({ name: "Ana" }) : problem(401, "unauthorized"))),
      http.post("/api/auth/refresh", async () => {
        refreshCalls++;
        await new Promise((resolve) => setTimeout(resolve, 20));
        refreshed = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const results = await Promise.all([apiJson("/api/auth/me"), apiJson("/api/auth/me"), apiJson("/api/auth/me")]);

    expect(results).toEqual([{ name: "Ana" }, { name: "Ana" }, { name: "Ana" }]);
    expect(log.requested).toHaveLength(3);
    expect(log.maxConcurrent).toBe(1);
    expect(refreshCalls).toBe(1);
  });

  it("gives up with the 401 when the refresh fails under the lock", async () => {
    installFakeLocks();
    server.use(
      http.get("/api/auth/me", () => problem(401, "unauthorized")),
      http.post("/api/auth/refresh", () => problem(401, "invalid_refresh_token")),
    );

    const error = await caught(apiJson("/api/auth/me"));

    expect(error.status).toBe(401);
    expect(error.code).toBe("unauthorized");
  });

  it("does not take the lock for requests that succeed", async () => {
    const log = installFakeLocks();
    server.use(http.get("/api/auth/me", () => HttpResponse.json({ name: "Ana" })));

    await apiJson("/api/auth/me");

    expect(log.requested).toEqual([]);
  });

  it("takes the same lock for the silent refresh of the login page", async () => {
    const log = installFakeLocks();
    server.use(http.post("/api/auth/refresh", () => new HttpResponse(null, { status: 204 })));

    await expect(trySilentRefresh()).resolves.toBe(true);

    expect(log.requested).toEqual([REFRESH_LOCK]);
  });

  it("reports a failed silent refresh as false, also when the server is unreachable", async () => {
    installFakeLocks();
    server.use(http.post("/api/auth/refresh", () => HttpResponse.error()));

    await expect(trySilentRefresh()).resolves.toBe(false);
  });
});
