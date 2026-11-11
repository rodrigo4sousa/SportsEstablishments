import { isValidElement, type ReactElement, type ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

// The page is called as a function here, outside React, so its `useTranslations` reads the real pt-PT catalog directly.
vi.mock("next-intl", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next-intl")>();
  const messages = (await import("../../../messages/pt-PT.json")).default;
  return { ...actual, useTranslations: (namespace?: string) => actual.createTranslator({ locale: "pt-PT", messages, namespace: namespace as never }) };
});
import { GuestOnly } from "@/components/guest-only";
import { ForgotPasswordForm } from "@/features/auth/components/forgot-password-form";
import Page from "./page";

/** Every element of a tree, depth first. */
function elements(node: ReactNode): ReactElement[] {
  if (Array.isArray(node)) return node.flatMap(elements);
  if (!isValidElement(node)) return [];
  const props = node.props as { children?: ReactNode };
  return [node, ...elements(props.children)];
}

describe("the forgot-password page", () => {
  it("puts the form inside GuestOnly, which sends a signed-in user to the home of their profile", () => {
    const guard = elements(Page()).find((element) => element.type === GuestOnly);

    expect(guard).toBeDefined();
    expect(elements(guard).some((element) => element.type === ForgotPasswordForm)).toBe(true);
  });
});
