"use client";

import Link from "next/link";
import { useEffect } from "react";
import { Button } from "@/components/ui/button";
import { PAGE_WIDTH } from "@/components/page-width";
import { useFocusOn } from "@/lib/focus";

type RouteErrorProps = {
  error: unknown;
  retry: () => void;
};

/**
 * The error page of a route: what happened in one sentence, a way to try again and a way out. It replaces the page, so it
 * takes focus (UX-002) instead of being a live region that would be read twice.
 */
export function RouteError({ error, retry }: RouteErrorProps) {
  const focus = useFocusOn<HTMLHeadingElement>();

  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <div className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-col items-start gap-4 px-4 py-12`}>
      <h1 {...focus} className="text-xl font-semibold">
        Ocorreu um erro inesperado.
      </h1>
      <p className="text-muted-foreground">Não foi possível mostrar esta página. Tenta novamente; se continuar, volta ao início.</p>
      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={() => retry()}>Tentar novamente</Button>
        <Button asChild variant="outline">
          <Link href="/">Voltar ao início</Link>
        </Button>
      </div>
    </div>
  );
}
