"use client";

import { useEffect } from "react";
import { Button } from "@/components/ui/button";

type RouteErrorProps = {
  error: unknown;
  retry: () => void;
};

export function RouteError({ error, retry }: RouteErrorProps) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <div role="alert" className="mx-auto flex w-full max-w-3xl flex-col items-start gap-4 px-4 py-12">
      <h2 className="text-xl font-semibold">Ocorreu um erro inesperado.</h2>
      <Button onClick={() => retry()}>Tentar novamente</Button>
    </div>
  );
}
