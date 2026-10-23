import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/msw";
import { getSessionAccount } from "./session";

let cookieHeader = "";
vi.mock("next/headers", () => ({
  cookies: async () => ({ toString: () => cookieHeader }),
}));

const account = { id: "0199f7a0-0000-7000-8000-0000000000aa", name: "Rui Dono", email: "rui@exemplo.pt", profile: "Owner" };
const ME = "http://localhost:5080/api/auth/me";

describe("getSessionAccount", () => {
  beforeEach(() => {
    cookieHeader = "access_token=abc; other=1";
    vi.unstubAllEnvs();
  });

  it("reads the account from the internal API with the user's own cookies, uncached", async () => {
    let received: { cookie: string | null } | null = null;
    server.use(
      http.get(ME, ({ request }) => {
        received = { cookie: request.headers.get("Cookie") };
        return HttpResponse.json(account);
      }),
    );
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    await expect(getSessionAccount()).resolves.toEqual(account);

    expect(received).toEqual({ cookie: "access_token=abc; other=1" });
    expect(fetchSpy).toHaveBeenCalledWith(ME, expect.objectContaining({ cache: "no-store" }));
    fetchSpy.mockRestore();
  });

  it("uses API_INTERNAL_URL when it is set", async () => {
    vi.stubEnv("API_INTERNAL_URL", "http://api:8080");
    server.use(http.get("http://api:8080/api/auth/me", () => HttpResponse.json(account)));

    await expect(getSessionAccount()).resolves.toEqual(account);
  });

  it("is no session when the API answers 401 (expired or invalid access token)", async () => {
    server.use(http.get(ME, () => HttpResponse.json({ status: 401, code: "unauthorized", traceId: "t" }, { status: 401 })));

    await expect(getSessionAccount()).resolves.toBeNull();
  });

  it("is no session, without calling the API, when the request has no cookies", async () => {
    cookieHeader = "";
    let calls = 0;
    server.use(
      http.get(ME, () => {
        calls++;
        return HttpResponse.json(account);
      }),
    );

    await expect(getSessionAccount()).resolves.toBeNull();
    expect(calls).toBe(0);
  });

  it("fails, instead of guessing, when the API is in trouble", async () => {
    server.use(http.get(ME, () => new HttpResponse(null, { status: 503 })));

    await expect(getSessionAccount()).rejects.toThrow("503");
  });

  it("never tries to refresh the session", async () => {
    let refreshes = 0;
    server.use(
      http.get(ME, () => HttpResponse.json({ status: 401, code: "unauthorized", traceId: "t" }, { status: 401 })),
      http.post("http://localhost:5080/api/auth/refresh", () => {
        refreshes++;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await getSessionAccount();

    expect(refreshes).toBe(0);
  });
});
