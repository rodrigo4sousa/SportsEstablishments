import { describe, expect, it } from "vitest";
import { formatPrice, parsePrice, priceInputValue } from "./price";

describe("parsePrice", () => {
  it.each([
    ["12,50", 12.5],
    ["12.50", 12.5],
    ["12", 12],
    ["12,5", 12.5],
    ["0,01", 0.01],
    ["500", 500],
    ["500,00", 500],
    ["  12,50 €", 12.5],
    ["12,50€", 12.5],
    ["12,500", 12.5],
    ["007,5", 7.5],
  ])("reads %j as %d euros", (typed, expected) => {
    expect(parsePrice(typed)).toEqual({ ok: true, value: expected });
  });

  it.each([
    ["", "required"],
    ["   ", "required"],
    ["abc", "invalid"],
    ["12,5,0", "invalid"],
    ["1.234,50", "invalid"],
    ["-5", "invalid"],
    ["12,", "invalid"],
    ["0", "notPositive"],
    ["0,00", "notPositive"],
    ["12,555", "decimals"],
    ["12.555", "decimals"],
    ["0,001", "decimals"],
    ["500,01", "tooHigh"],
    ["501", "tooHigh"],
    ["9999", "tooHigh"],
  ])("rejects %j with one reason", (typed, error) => {
    expect(parsePrice(typed)).toEqual({ ok: false, error });
  });
});

describe("price display", () => {
  it("formats euros the pt-PT way, with a comma and the symbol after", () => {
    expect(formatPrice(12.5, "pt-PT").replace(/\s/g, " ")).toBe("12,50 €");
    expect(formatPrice(8, "pt-PT").replace(/\s/g, " ")).toBe("8,00 €");
    expect(formatPrice(0.5, "pt-PT").replace(/\s/g, " ")).toBe("0,50 €");
  });

  it("puts a price in the edit field with a comma and two decimals", () => {
    expect(priceInputValue(12.5, "pt-PT")).toBe("12,50");
    expect(priceInputValue(8, "pt-PT")).toBe("8,00");
  });

  it("reads back what it wrote", () => {
    expect(parsePrice(priceInputValue(18.75, "pt-PT"))).toEqual({ ok: true, value: 18.75 });
  });
});

describe("price display in English (T09)", () => {
  it("writes euros the en-GB way, with the symbol first and a decimal point", () => {
    expect(formatPrice(12.5, "en")).toBe("€12.50");
    expect(priceInputValue(12.5, "en")).toBe("12.50");
  });

  it("reads an English price, symbol first or last", () => {
    expect(parsePrice("€12.50")).toEqual({ ok: true, value: 12.5 });
    expect(parsePrice(priceInputValue(18.75, "en"))).toEqual({ ok: true, value: 18.75 });
  });
});
