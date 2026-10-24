import { describe, expect, it } from "vitest";
import { isClubSlug } from "./slug";

describe("isClubSlug", () => {
  it.each(["clube-da-foz", "padel-co", "clube", "clube-evora-2", "a1-b2-c3", "x".repeat(91)])("accepts %s", (slug) => {
    expect(isClubSlug(slug)).toBe(true);
  });

  it.each([
    "",
    "Clube-Da-Foz", // the API makes lower-case slugs only
    "clube_da_foz",
    "clube da foz",
    "-clube",
    "clube-",
    "clube--foz",
    "clube/foz",
    "clube..foz",
    "évora",
    "x".repeat(92),
  ])("rejects %j without asking the API", (slug) => {
    expect(isClubSlug(slug)).toBe(false);
  });
});
