"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { loginUrl } from "@/lib/auth/redirects";
import { RegisterForm } from "./register-form";

/**
 * The player sign-up page body. It reads the `next` search parameter in the browser, like the login screen, so a visitor
 * who was sent from a club page (and chose "Criar conta" on the login page) returns to the same slot afterwards, and the
 * way back to signing in keeps it too.
 */
export function RegisterScreen() {
  const next = useSearchParams().get("next");

  return (
    <>
      <RegisterForm next={next} />
      <p className="text-sm text-muted-foreground">
        Já tens conta?{" "}
        <Link href={next ? loginUrl(next) : "/login"} className="underline underline-offset-4">
          Entrar
        </Link>
      </p>
    </>
  );
}
