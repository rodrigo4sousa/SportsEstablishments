# 0001 · Uma só aplicação com três áreas

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem da [Stack](../../stack.md), de 2026-10-22).
- **Elementos:** C-WEB, C-API, WEB-AREA-PLAYER, WEB-AREA-MANAGE, WEB-AREA-ADMIN, WEB-AREA-ACCOUNT, WEB-GUARDS, WEB-PROXY, CC-AUTHZ.

## Contexto

O produto tem três perfis com ecrãs muito diferentes: o jogador pesquisa e reserva, o dono gere clubes e o admin vê números ([backlog](../../backlog.md), «Perfis»). Todos falam com a mesma API, que é a única dona das regras de negócio, e uma conta tem um só perfil.

Alternativas:

1. **Uma aplicação Next.js por perfil** (três projetos e três deploys). Isola o código de cada perfil, mas repete o cliente da API, o login, os formulários e a configuração, e obriga a manter três builds.
2. **Uma aplicação para o público e outra para a gestão** (dono e admin). Metade da duplicação, e o mesmo problema.
3. **Uma só aplicação com as três áreas por rota.**

## Decisão

Opção 3: **um só Next.js (C-WEB)** com as áreas por prefixo de rota sobre **uma só API (C-API)**: `/` e `/clubs/[slug]` (jogador, público), `/manage` (dono), `/admin` (admin), mais as páginas de conta (`/login`, `/register`, …). O código vive em `frontend/src/app/` (um grupo de rotas por área) e `frontend/src/features/` (uma pasta por funcionalidade).

O acesso a cada área é decidido em dois níveis:

- **No servidor do Next.js, por conveniência:** os layouts de `/manage`, `/admin` e `/bookings` têm um guarda (`AreaGuard`, `PlayerGuard`) que redireciona para `/login?next=…` ou para a área do perfil certo; o `proxy.ts` só escreve o caminho pedido em `x-pathname` para isso.
- **Na API, que decide sempre:** uma política de autorização por perfil (`Player`, `Owner`, `Admin`) e, no dono, a verificação por clube (`OwnedClubs`, T02). Outro perfil recebe 403; um recurso de outro dono, 404.

## Consequências

**Boas**

- Um só login, um só cliente da API (`lib/api/client.ts`), um só conjunto de componentes e de tipos gerados, um só build e um só deploy.
- Uma só origem para o browser, o que o desenho dos cookies precisa ([ADR 0002](0002-sessao-em-cookies-httponly.md)).
- Os guardas são só conveniência: se falharem, a API continua a recusar.

**Más ou a vigiar**

- O código das três áreas vai no mesmo pacote: a separação é por pastas e por convenção, não imposta por projetos.
- Os guardas lêem cookies, por isso ficam dentro de `<Suspense>` por causa do `cacheComponents`; um redirecionamento é então enviado como `<meta http-equiv="refresh">` e não como 307 ([vista de processo](../4plus1/process.md#27-página-de-uma-área-com-guarda-no-servidor)).
- Quem muda uma funcionalidade partilhada (por exemplo `formatPrice`) toca em várias áreas.
