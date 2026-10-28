import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/msw";
import { fetchSlots } from "./api";

describe("fetchSlots", () => {
  it("gives each slot's price as a number: the band price or the base price the API decided (US10)", async () => {
    server.use(
      http.get("/api/manage/clubs/c1/pitches/p1/slots", () =>
        HttpResponse.json({
          date: "2026-10-27",
          slotDurationMinutes: "60",
          slots: [
            { startsAt: "2026-10-27T09:00:00Z", endsAt: "2026-10-27T10:00:00Z", blocked: false, booked: false, price: "20.00" },
            { startsAt: "2026-10-27T10:00:00Z", endsAt: "2026-10-27T11:00:00Z", blocked: false, booked: true, price: 35.5 },
          ],
        }),
      ),
    );

    const result = await fetchSlots("c1", "p1", "2026-10-27");

    expect(result.slotDurationMinutes).toBe(60);
    expect(result.slots.map((slot) => slot.price)).toEqual([20, 35.5]);
  });
});
