# 0002 · Sessão em cookies httpOnly em vez de localStorage

- **Estado:** Aceite.
- **Data:** 2026-10-30 (registo; a decisão vem da [Stack](../../stack.md), «Autenticação: cookies httpOnly», e da US03).
- **Elementos:** C-BROWSER, C-WEB, C-API, API-AUTH, CC-COOKIES, CC-ORIGIN, CC-RATE, WEB-API-CLIENT, WEB-GUARDS, D-REFRESH.

## Contexto

A aplicação precisa de saber quem está autenticado em todos os pedidos, e o JavaScript da página não deve conseguir roubar a sessão se houver um ataque XSS. O front end tem partes no servidor (Server Components, que chamam a API) e partes no browser.

Alternativas:

1. **Tokens em `localStorage` ou `sessionStorage`**, enviados em `Authorization: Bearer`. Qualquer script injetado os lê; os Server Components não os veem.
2. **Sessão de servidor** (um identificador opaco numa tabela, sem JWT). Resolveria o ponto anterior, mas obriga a ir à base de dados em cada pedido.
3. **JWT curto e refresh token rotativo, ambos em cookies httpOnly.**

## Decisão

Opção 3 ([Stack](../../stack.md)):

- `access_token`: JWT HS256 com `sub` e `role`, 15 minutos, `HttpOnly; Secure; SameSite=Lax; Path=/`. O JwtBearer lê-o do cookie.
- `refresh_token`: 32 bytes aleatórios guardados só como SHA-256 (`refresh_tokens`), 7 dias de validade absoluta, `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. Cada refresh roda o token por uma atualização condicional; um token já usado que volte a aparecer (no refresh ou no logout) revoga todas as sessões da conta.
- O browser sabe quem é o utilizador por `GET /api/auth/me`; nunca lê os tokens.
- **Mesma origem, sem CORS:** as páginas e `/api/*` vêm do mesmo endereço (hoje, os rewrites do Next.js), para os cookies serem first-party. A API recusa `POST`/`PUT`/`PATCH`/`DELETE` cujo `Origin` não esteja em `Security:AllowedOrigins` (403 `origin_not_allowed`), o que, com `SameSite`, protege do CSRF.
- Contra a força bruta: bloqueio de conta do Identity (5 erros, 15 minutos) e limites de pedidos por IP nas rotas de autenticação.

## Consequências

**Boas**

- Um XSS não lê os tokens.
- Os Server Components passam o cookie do utilizador à API; não há um segundo mecanismo para o servidor.
- Uma conta desativada deixa de renovar a sessão e as suas sessões são revogadas.

**Más ou a vigiar**

- **O servidor nunca renova a sessão**, porque o cookie do refresh só vai para `/api/auth` e as páginas não o levam. Os refreshes vêm sempre do browser: um 401 faz um só refresh e repete o pedido, com um Web Lock entre separadores (senão o segundo separador apresentaria um token já usado e a conta ficaria sem sessões), e `/login` tenta um refresh silencioso antes do formulário.
- **Compromisso aceite:** o access token é validado sem ir à base de dados, por isso vale até 15 minutos depois de a conta ser desativada ou de a palavra-passe mudar.
- Chamadas feitas pelo servidor que alterem dados (Route Handlers, Server Actions) teriam de enviar o `Origin` do site; hoje não há nenhuma.
- A segurança dos cookies depende de uma só origem: o desenho de produção ([ADR 0009](0009-alvo-de-implantacao.md)) tem de a manter.
