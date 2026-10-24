import { describe, expect, it } from "vitest";
import { PRICE_MESSAGES, formatPrice, parsePrice, priceInputValue } from "./price";

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
    ["", PRICE_MESSAGES.required],
    ["   ", PRICE_MESSAGES.required],
    ["abc", PRICE_MESSAGES.invalid],
    ["12,5,0", PRICE_MESSAGES.invalid],
    ["1.234,50", PRICE_MESSAGES.invalid],
    ["-5", PRICE_MESSAGES.invalid],
    ["12,", PRICE_MESSAGES.invalid],
    ["0", PRICE_MESSAGES.notPositive],
    ["0,00", PRICE_MESSAGES.notPositive],
    ["12,555", PRICE_MESSAGES.decimals],
    ["12.555", PRICE_MESSAGES.decimals],
    ["0,001", PRICE_MESSAGES.decimals],
    ["500,01", PRICE_MESSAGES.tooHigh],
    ["501", PRICE_MESSAGES.tooHigh],
    ["9999", PRICE_MESSAGES.tooHigh],
  ])("rejects %j with one message", (typed, message) => {
    expect(parsePrice(typed)).toEqual({ ok: false, message });
  });
});

describe("price display", () => {
  it("formats euros the pt-PT way, with a comma and the symbol after", () => {
    expect(formatPrice(12.5).replace(/\s/g, " ")).toBe("12,50 €");
    expect(formatPrice(8).replace(/\s/g, " ")).toBe("8,00 €");
    expect(formatPrice(0.5).replace(/\s/g, " ")).toBe("0,50 €");
  });

  it("puts a price in the edit field with a comma and two decimals", () => {
    expect(priceInputValue(12.5)).toBe("12,50");
    expect(priceInputValue(8)).toBe("8,00");
  });

  it("reads back what it wrote", () => {
    expect(parsePrice(priceInputValue(18.75))).toEqual({ ok: true, value: 18.75 });
  });
});
