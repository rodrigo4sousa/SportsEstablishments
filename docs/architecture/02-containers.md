# 02 · Contentores

<!-- C4 nível 2 · vista 4+1: de processo. Ver README.md. -->

Dois diagramas: o que corre hoje em desenvolvimento e o que corre em produção. Em ambos o browser vê **uma só origem**, o que mantém os cookies de sessão first-party e dispensa CORS.

## Diagrama

### Desenvolvimento (docker compose)

![Contentores em desenvolvimento](diagrams/02-containers-dev.svg)

Fonte: [02-containers-dev.puml](diagrams/02-containers-dev.puml).

### Produção (T06)

![Contentores em produção (T06)](diagrams/02-containers-prod.svg)

Fonte: [02-containers-prod.puml](diagrams/02-containers-prod.puml). **Em produção desde 2026-11-09 (T06).**

## Contentores

| Id | Tecnologia | Responsabilidade | Dono de |
| --- | --- | --- | --- |
| C-BROWSER | Browser | Corre os Client Components e guarda os cookies httpOnly `access_token` e `refresh_token`. Fala só com C-WEB (em desenvolvimento, também com a API na 5080, para o Swagger). | Os cookies de sessão. |
| C-WEB | Next.js 16 (App Router, imagem `standalone`), porta 3000 | Serve as áreas do jogador, do dono e do admin. Os Server Components chamam a API pelo URL interno, a passar o cookie do utilizador; os Client Components chamam `/api/*` na mesma origem. Reencaminha `/api/*` para a API (WEB-REWRITE). | Nenhum dado: não acede à base de dados. |
| C-API | .NET 10, ASP.NET Core, porta 8080 (5080 no anfitrião, só em desenvolvimento) | Única dona das regras de negócio, da autenticação (JWT e refresh em cookies), da autorização por perfil e por dono, dos erros ProblemDetails e do rate limiting. Verifica o `Origin` dos pedidos que alteram dados. | Todas as regras e todo o acesso a C-DB. |
| C-WORKER | `BackgroundService` no processo da API | Lê o outbox, compõe e envia os emails com novas tentativas; usa `SKIP LOCKED`, por isso pode correr em várias instâncias. | O envio dos emails (o outbox em si está em C-DB). |
| C-DB | PostgreSQL 18.6 (desenvolvimento); PostgreSQL gerido (produção, T06) | Guarda os dados, impõe as regras que não podem falhar (sem reservas confirmadas sobrepostas, histórico de donos só de acrescentar) e guarda as chaves do Data Protection. | Os dados e o esquema. |

C-CLI (`seed-demo` e `set-admin-password`) é o mesmo executável da API sem arrancar o servidor, por isso não tem caixa própria nestes diagramas.

## Comunicação entre contentores

**Desenvolvimento**

- C-BROWSER → C-WEB: HTTP na porta 3000, com cookies. É a entrada normal.
- C-WEB → C-API: o Next.js reencaminha `/api/*` para `http://api:8080`, e os Server Components leem a API no mesmo endereço (`API_INTERNAL_URL`). O endereço é argumento de build (os rewrites ficam fixados no build) e variável de ambiente em execução.
- C-BROWSER → C-API: a API também está publicada na porta 5080 (Swagger em `/swagger`), e `http://localhost:5080` é uma das origens aceites em Development. É uma entrada direta para ferramentas, fora do caminho normal.
- C-API e C-WORKER → C-DB: SQL com Npgsql, `Host=postgres;Port=5432`.
- C-WORKER → X-MAILPIT: SMTP na porta 1025, sem TLS; a interface web está na 8025.

**Produção (T06)**

- C-BROWSER → C-WEB: HTTPS, na origem da Vercel (PL-VERCEL). É a única origem que o browser conhece.
- C-WEB → C-API: HTTPS pela Internet, para o URL público do serviço da API no Render (PL-RENDER-API, `API_INTERNAL_URL`), tanto no rewrite de `/api/*` como nos Server Components. A API também se alcança diretamente: o `Origin` e os limites protegem-na, e os limites por IP são só de melhor esforço.
- C-API e C-WORKER → C-DB (PL-SUPABASE): SQL pelo pooler em modo sessão, como `sportsbooking_app`.
- C-WORKER → PL-BREVO: SMTP na porta 2525 com StartTls (a confirmar), porque o plano gratuito do Render bloqueia as portas 25, 465 e 587.
- Cookies: são httpOnly e Secure; o `refresh_token` tem `Path=/api/auth`. Como o browser só conhece a origem da Vercel, são first-party. A API verifica que o `Origin` é o do site (`Security__AllowedOrigins__0`).

## Notas

- **O proxy reverso é o Next.js, não o Caddy.** Em desenvolvimento e no compose é C-WEB que reencaminha `/api/*`; em produção é o rewrite do Next.js, servido pela Vercel. O desenho antigo (Caddy numa máquina virtual, em `docs/stack.md`) não foi adotado (ver a [ADR 0009](adr/0009-alvo-de-implantacao.md)).
- **O IP do cliente.** Sem proxy configurado, a API ignora `X-Forwarded-For` e usa o endereço da ligação; é assim no compose, porque o rewrite passa à frente um `X-Forwarded-For` enviado pelo browser. Em produção a configuração é outra e é provisória (ver a [secção 4.4 da vista física](06-physical-view.md#44-o-ip-do-cliente)).
- O worker corre no processo da API: em produção (T06) os emails e lembretes só saem enquanto a API estiver acordada.
- As migrações correm no arranque da API só em desenvolvimento. Em produção aplica-as o GitHub Actions, com um migration bundle, como dono do esquema (ver a [secção 4.8 da vista física](06-physical-view.md#48-onde-correm-as-migrações-e-os-comandos)).
