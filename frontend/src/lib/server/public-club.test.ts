import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { NotFoundSignal } from "@/test/navigation";
import { server } from "@/test/msw";
import { getPublicClub, loadPublicClub } from "./public-club";

vi.mock("next/navigation", async () => import("@/test/navigation"));

const API = "http://localhost:5080";

const clubBody = {
  slug: "clube-da-foz",
  name: "Clube da Foz",
  address: "Rua do Mar 1",
  locality: "Porto",
  phone: "912345678",
  contactEmail: "geral@foz.pt",
  pitches: [{ id: "0199f7a0-0000-7000-8000-0000000000d1", name: "Padel 1", sport: "padel", pricePerSlot: "18.50" }],
};

describe("getPublicClub", () => {
  it("reads the club from the internal API without sending any cookie, and turns prices into numbers", async () => {
    let received: Request | undefined;
    server.use(
      http.get(`${API}/api/clubs/clube-da-foz`, ({ request }) => {
        received = request;
        return HttpResponse.json(clubBody);
      }),
    );
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    const club = await getPublicClub("clube-da-foz");

    expect(club?.name).toBe("Clube da Foz");
    expect(club?.pitches[0].pricePerSlot).toBe(18.5);
    expect(received?.headers.get("cookie")).toBeNull();
    // Never cached: a club that has just been hidden, or a new price, must show at once (docs/stack.md).
    expect(fetchSpy).toHaveBeenCalledWith(`${API}/api/clubs/clube-da-foz`, expect.objectContaining({ cache: "no-store" }));
    fetchSpy.mockRestore();
  });

  it("is null for a club the API does not have", async () => {
    server.use(http.get(`${API}/api/clubs/nao-existe`, () => HttpResponse.json({ code: "club_not_found" }, { status: 404 })));

    expect(await getPublicClub("nao-existe")).toBeNull();
  });

  it("throws on any other failure, so the error page shows and not a wrong not-found", async () => {
    server.use(http.get(`${API}/api/clubs/clube-da-foz`, () => new HttpResponse(null, { status: 500 })));

    await expect(getPublicClub("clube-da-foz")).rejects.toThrow("failed with 500");
  });
});

describe("loadPublicClub", () => {
  it("gives the club", async () => {
    server.use(http.get(`${API}/api/clubs/clube-da-foz`, () => HttpResponse.json(clubBody)));

    await expect(loadPublicClub("clube-da-foz")).resolves.toMatchObject({ slug: "clube-da-foz" });
  });

  it("shows the not-found page for a club the API does not have", async () => {
    server.use(http.get(`${API}/api/clubs/nao-existe`, () => HttpResponse.json({ code: "club_not_found" }, { status: 404 })));

    await expect(loadPublicClub("nao-existe")).rejects.toBeInstanceOf(NotFoundSignal);
  });

  it("shows the not-found page for a slug that cannot exist, without asking the API", async () => {
    // No handler: the test fails if a request is made.
    for (const slug of ["Clube-Da-Foz", "clube/foz", "..", "x".repeat(200)]) {
      await expect(loadPublicClub(slug), slug).rejects.toBeInstanceOf(NotFoundSignal);
    }
  });
});
