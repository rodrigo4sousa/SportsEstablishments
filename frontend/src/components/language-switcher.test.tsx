import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithIntl } from "@/test/intl";
import { LanguageSwitcher } from "./language-switcher";

describe("the language switcher (T09)", () => {
  const reload = vi.fn();
  const original = window.location;

  beforeEach(() => {
    document.cookie = "NEXT_LOCALE=; Path=/; Max-Age=0";
    reload.mockClear();
    Object.defineProperty(window, "location", { configurable: true, value: { ...original, protocol: "https:", reload } });
  });

  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: original });
  });

  it("is a labelled group of two buttons, «PT» and «EN», each named with its language, the current one marked", () => {
    renderWithIntl(<LanguageSwitcher />);

    const group = screen.getByRole("group", { name: "Idioma" });
    const pt = within(group).getByRole("button", { name: "PT – Português" });
    const en = within(group).getByRole("button", { name: "EN – English" });
    expect(pt).toHaveAttribute("aria-current", "true");
    expect(en).not.toHaveAttribute("aria-current");
    expect(pt).toHaveAttribute("lang", "pt-PT");
    expect(en).toHaveAttribute("lang", "en");
    // What the eye sees is «PT | EN»; the language names are for screen readers only.
    expect([...group.querySelectorAll("button")].map((button) => button.firstChild?.textContent)).toEqual(["PT", "EN"]);
    expect(group.querySelector("[aria-hidden]")).toHaveTextContent("|");
  });

  it("says «Language» and marks EN on an English page", () => {
    renderWithIntl(<LanguageSwitcher />, "en");

    const group = screen.getByRole("group", { name: "Language" });
    expect(within(group).getByRole("button", { name: "EN – English" })).toHaveAttribute("aria-current", "true");
  });

  it("works with the keyboard: choosing English writes the cookie and loads the page again", async () => {
    const user = userEvent.setup();
    renderWithIntl(<LanguageSwitcher />);

    await user.tab();
    expect(screen.getByRole("button", { name: "PT – Português" })).toHaveFocus();
    await user.tab();
    await user.keyboard("{Enter}");

    // EN, after PT: Enter on it switches (what the cookie holds is checked below).
    expect(screen.getByRole("button", { name: "EN – English" })).toHaveFocus();
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it("writes NEXT_LOCALE only when a different language is chosen", async () => {
    const written: string[] = [];
    const descriptor = Object.getOwnPropertyDescriptor(Document.prototype, "cookie")!;
    Object.defineProperty(document, "cookie", { configurable: true, get: () => "", set: (value: string) => written.push(value) });
    const user = userEvent.setup();
    renderWithIntl(<LanguageSwitcher />);

    await user.click(screen.getByRole("button", { name: "PT – Português" }));
    expect(written).toEqual([]);
    expect(reload).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "EN – English" }));
    expect(written).toEqual(["NEXT_LOCALE=en; Path=/; Max-Age=31536000; SameSite=Lax; Secure"]);
    expect(reload).toHaveBeenCalledTimes(1);

    Object.defineProperty(document, "cookie", descriptor);
    delete (document as { cookie?: string }).cookie;
  });
});
