"use client";

import "./globals.css";

/**
 * The last resort: an error in the root layout itself. It replaces the whole document, so it brings its own `<html>` and
 * `<body>`, and uses nothing from the layout (no providers, no header), only the stylesheet.
 */
export default function GlobalError({ error, retry }: { error: unknown; retry: () => void }) {
  console.error(error);

  return (
    <html lang="pt-PT">
      <body>
        <title>Erro · Reservas de Campos</title>
        <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col items-start justify-center gap-4 px-4 py-12">
          <h1 className="text-2xl font-semibold tracking-tight">Ocorreu um erro inesperado.</h1>
          <p className="text-muted-foreground">Não foi possível mostrar a aplicação. Tenta outra vez; se continuar, volta mais tarde.</p>
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={() => retry()}
              className="inline-flex h-9 items-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground"
            >
              Tentar novamente
            </button>
            {/* A plain link on purpose: the root layout failed, so there may be no router, and a full page load starts clean. */}
            {/* eslint-disable-next-line @next/next/no-html-link-for-pages */}
            <a href="/" className="inline-flex h-9 items-center rounded-md border px-4 text-sm font-medium">
              Voltar ao início
            </a>
          </div>
        </main>
      </body>
    </html>
  );
}
