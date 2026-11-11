import { describe, expect, it } from "vitest";
import { intlLocale, isAppLocale, localeCookie, localeFromAcceptLanguage, resolveLocale } from "./locales";

describe("the language of a request (T09)", () => {
  it("is the cookie the switcher wrote, whatever the browser asks for", () => {
    expect(resolveLocale({ cookie: "en", acceptLanguage: "pt-PT,pt;q=0.9" })).toBe("en");
    expect(resolveLocale({ cookie: "pt-PT", acceptLanguage: "en-GB,en;q=0.9" })).toBe("pt-PT");
  });

  it("ignores a cookie with any other value", () => {
    expect(resolveLocale({ cookie: "fr", acceptLanguage: "en-US" })).toBe("en");
    expect(resolveLocale({ cookie: "EN", acceptLanguage: null })).toBe("pt-PT");
    expect(resolveLocale({ cookie: "", acceptLanguage: "pt-BR" })).toBe("pt-PT");
  });

  it.each([
    ["en", "en"],
    ["en-US,en;q=0.9", "en"],
    ["en-GB", "en"],
    ["pt-PT,pt;q=0.9,en;q=0.8", "pt-PT"],
    ["pt-BR", "pt-PT"],
    ["fr-FR,fr;q=0.9,en;q=0.5", "en"],
    ["de-DE,pt;q=0.4,en;q=0.3", "pt-PT"],
    ["en;q=0.2,pt;q=0.8", "pt-PT"],
    ["EN-us", "en"],
  ] as const)("without a cookie follows Accept-Language %j: %s", (header, expected) => {
    expect(resolveLocale({ cookie: undefined, acceptLanguage: header })).toBe(expected);
  });

  it.each([null, "", "de-DE,fr;q=0.8", "*", "en;q=0", "  "])("falls back to pt-PT for %j", (header) => {
    expect(resolveLocale({ acceptLanguage: header })).toBe("pt-PT");
    expect(localeFromAcceptLanguage(header)).toBeNull();
  });

  it("formats with pt-PT and en-GB", () => {
    expect(intlLocale("pt-PT")).toBe("pt-PT");
    expect(intlLocale("en")).toBe("en-GB");
    expect(isAppLocale("en")).toBe(true);
    expect(isAppLocale("en-GB")).toBe(false);
  });

  it("writes a one-year, site-wide, Lax cookie, Secure on HTTPS", () => {
    expect(localeCookie("en", true)).toBe("NEXT_LOCALE=en; Path=/; Max-Age=31536000; SameSite=Lax; Secure");
    expect(localeCookie("pt-PT", false)).toBe("NEXT_LOCALE=pt-PT; Path=/; Max-Age=31536000; SameSite=Lax");
  });
});
