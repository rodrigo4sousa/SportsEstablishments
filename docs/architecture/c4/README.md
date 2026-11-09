# C4: contexto, contentores e implantação

<!-- Modelo C4 · níveis 1 (contexto) e 2 (contentores) e diagramas de implantação. Ver README.md. -->

Os diagramas C4 mais gerais do sistema: o contexto (nível 1), os contentores (nível 2) e a implantação, em desenvolvimento e em produção. Os componentes (nível 3) e o código (nível 4) estão na [vista lógica](../4plus1/logical.md), e os diagramas dinâmicos, em sequência, na [vista de processo](../4plus1/process.md). Os ids estão no [inventário](../inventory.md); os `.puml` e os SVG de todos os diagramas estão ao lado, em `c4/` e `4plus1/` (fontes) e em `out/` (desenhos).

Para cada nível, para quem é e que decisão documenta:

- **Contexto** (nível 1). **Para:** quem avalia, produto, recém-chegados. **Decisão documentada:** um só marketplace, com uma conta de jogador que serve em todos os clubes de todos os donos e sem sites por dono ([ADR 0005](../adr/0005-marketplace-unico.md)), e três áreas (jogador, dono, admin) sobre a mesma API ([ADR 0001](../adr/0001-uma-app-tres-areas.md)).
- **Contentores** (nível 2). **Para:** recém-chegados e quem opera. **Decisão documentada:** o browser vê uma só origem, o que mantém os cookies de sessão first-party e dispensa CORS ([ADR 0002](../adr/0002-sessao-em-cookies-httponly.md)); o worker de emails corre no processo da API e lê o outbox ([ADR 0004](../adr/0004-outbox-para-emails.md)).
- **Implantação.** **Para:** quem faz deploy e opera. **Decisão documentada:** a produção na Vercel, no Render e no Supabase, com o Brevo para o email ([ADR 0009](../adr/0009-alvo-de-implantacao.md)); o desenvolvimento é o docker compose.

## Nível 1 · Contexto do sistema

<!-- C4 nível 1 · vista 4+1: cenários (contexto de uso). Ver README.md. -->

### Diagrama do contexto

![Contexto do sistema](../out/01-context.svg)

Fonte: [01-context.puml](01-context.puml). Todos os elementos existem; os de produção (ids `PL-`) estão em funcionamento desde 2026-11-09 (T06).

### Âmbito

A App de Reservas de Campos é um **mercado único**: um só sistema onde jogadores reservam campos (futsal, futebol 7, padel, ténis) em clubes portugueses. Um jogador tem uma conta que serve em todos os clubes de todos os donos; não há sites nem domínios por dono. O mesmo sistema tem três áreas (jogador, dono, admin) sobre a mesma API. Não há pagamentos: a reserva guarda o preço do momento.

Fora do âmbito: pagamentos, aplicações móveis, integração com outros sistemas de reservas, reservas depois da meia-noite (limitação conhecida do MVP) e qualquer forma de o admin ver dados pessoais dos jogadores.

### Pessoas

| Id | Papel | O que faz com o sistema |
| --- | --- | --- |
| P-VIS | Visitante | Pesquisa clubes e vê horários livres; para reservar vai ao login. |
| P-JOG | Jogador | Reserva (um slot ou uma série semanal), vê e cancela as suas reservas, recebe emails. |
| P-DONO | Dono | Gere clubes, campos, horários, bloqueios, preços e o calendário; reserva para clientes; marca faltas. |
| P-ADMIN | Admin | Vê contagens e gráficos e desativa ou reativa donos. Só existe por seed. |
| P-CLI | Cliente sem conta | Não usa o sistema: pede ao clube, e o dono reserva em nome dele. Não recebe email. |
| P-OPS | Operador | Arranca a aplicação, define a configuração e corre `seed-demo` e `set-admin-password`. |

### Sistemas externos

| Id | Sistema | Ligação |
| --- | --- | --- |
| X-SMTP | Servidor SMTP | A aplicação envia por SMTP os emails de reservas e de contas. Em desenvolvimento é o X-MAILPIT (nada sai para fora); em produção é o PL-BREVO. |
| PL-BREVO | Brevo | O servidor SMTP de produção. |
| PL-VERCEL | Vercel | A plataforma onde o site corre. É só uma caixa: o que contém está nos [contentores](#diagrama-dos-contentores). |
| PL-RENDER | Render | A plataforma onde a API corre. É só uma caixa: o que contém está nos [contentores](#diagrama-dos-contentores). |

O CI (X-GHA) e o repositório (X-GH) não fazem parte do uso do sistema e não aparecem aqui; estão na vista de implantação.

### Notas do contexto

- A Vercel e o Render (T06) aparecem como sistemas externos para marcar que o alojamento é uma escolha de implantação e não do sistema. Nenhuma regra de negócio depende deles.
- O envio de email nunca bloqueia uma reserva: a aplicação grava o email num outbox e envia-o depois (ver [4plus1/process.md](../4plus1/process.md)).

## Nível 2 · Contentores

<!-- C4 nível 2 · vista 4+1: de processo. Ver README.md. -->

Dois diagramas: o que corre hoje em desenvolvimento e o que corre em produção. Em ambos o browser vê **uma só origem**, o que mantém os cookies de sessão first-party e dispensa CORS.

### Diagrama dos contentores

#### Desenvolvimento (docker compose)

![Contentores em desenvolvimento](../out/02-containers-dev.svg)

Fonte: [02-containers-dev.puml](02-containers-dev.puml).

#### Produção (T06)

![Contentores em produção (T06)](../out/02-containers-prod.svg)

Fonte: [02-containers-prod.puml](02-containers-prod.puml). **Em produção desde 2026-11-09 (T06).**

### Contentores

| Id | Tecnologia | Responsabilidade | Dono de |
| --- | --- | --- | --- |
| C-BROWSER | Browser | Corre os Client Components e guarda os cookies httpOnly `access_token` e `refresh_token`. Fala só com C-WEB (em desenvolvimento, também com a API na 5080, para o Swagger). | Os cookies de sessão. |
| C-WEB | Next.js 16 (App Router, imagem `standalone`), porta 3000 | Serve as áreas do jogador, do dono e do admin. Os Server Components chamam a API pelo URL interno, a passar o cookie do utilizador; os Client Components chamam `/api/*` na mesma origem. Reencaminha `/api/*` para a API (WEB-REWRITE). | Nenhum dado: não acede à base de dados. |
| C-API | .NET 10, ASP.NET Core, porta 8080 (5080 no anfitrião, só em desenvolvimento) | Única dona das regras de negócio, da autenticação (JWT e refresh em cookies), da autorização por perfil e por dono, dos erros ProblemDetails e do rate limiting. Verifica o `Origin` dos pedidos que alteram dados. | Todas as regras e todo o acesso a C-DB. |
| C-WORKER | `BackgroundService` no processo da API | Lê o outbox, compõe e envia os emails com novas tentativas; usa `SKIP LOCKED`, por isso pode correr em várias instâncias. | O envio dos emails (o outbox em si está em C-DB). |
| C-DB | PostgreSQL 18.6 (desenvolvimento); PostgreSQL gerido (produção, T06) | Guarda os dados, impõe as regras que não podem falhar (sem reservas confirmadas sobrepostas, histórico de donos só de acrescentar) e guarda as chaves do Data Protection. | Os dados e o esquema. |

C-CLI (`seed-demo` e `set-admin-password`) é o mesmo executável da API sem arrancar o servidor, por isso não tem caixa própria nestes diagramas.

### Comunicação entre contentores

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

### Notas dos contentores

- **O proxy reverso é o Next.js, não o Caddy.** Em desenvolvimento e no compose é C-WEB que reencaminha `/api/*`; em produção é o rewrite do Next.js, servido pela Vercel. O desenho antigo (Caddy numa máquina virtual, em `docs/stack.md`) não foi adotado (ver a [ADR 0009](../adr/0009-alvo-de-implantacao.md)).
- **O IP do cliente.** Sem proxy configurado, a API ignora `X-Forwarded-For` e usa o endereço da ligação; é assim no compose, porque o rewrite passa à frente um `X-Forwarded-For` enviado pelo browser. Em produção a configuração é outra e é provisória (ver a [secção 4.4 da vista física](../4plus1/physical.md#44-o-ip-do-cliente)).
- O worker corre no processo da API: em produção (T06) os emails e lembretes só saem enquanto a API estiver acordada.
- As migrações correm no arranque da API só em desenvolvimento. Em produção aplica-as o GitHub Actions, com um migration bundle, como dono do esquema (ver a [secção 4.8 da vista física](../4plus1/physical.md#48-onde-correm-as-migrações-e-os-comandos)).

## Implantação

<!-- Diagramas de implantação do C4 · vista 4+1: física. Ver README.md. -->

Onde corre cada contentor, em desenvolvimento (que existe) e em produção (em funcionamento desde 2026-11-09, T06). As secções são as da [vista física](../4plus1/physical.md), que tem as restantes: a 2 (integração contínua), a 4 (rede e confiança), a 5 (riscos) e a 6 (notas).

### 1. Desenvolvimento (docker compose)

![Implantação em desenvolvimento](../out/04-deployment-dev.svg)

Fonte: [04-deployment-dev.puml](04-deployment-dev.puml).

Uma máquina (DEP-DEVHOST) com Docker. `docker compose up --build` cria o projeto `sportsbooking` com a rede por omissão do compose (DEP-COMPOSE-NET), onde os serviços se chamam pelo nome (`api`, `postgres`, `mailpit`).

| Serviço | Elemento | Imagem ou build | Portas (anfitrião → contentor) | Volumes | Arranca depois de | Ambiente (do `docker-compose.yml`) |
| --- | --- | --- | --- | --- | --- | --- |
| `postgres` | C-DB | `postgres:18.6` | `5432 → 5432` | `postgres-data` (DEP-PGVOLUME) em `/var/lib/postgresql` | — | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` (do `.env`, por omissão `sportsbooking`). Health check `pg_isready` a cada 5 s. |
| `mailpit` | X-MAILPIT | `axllent/mailpit:v1.31.6` | `8025 → 8025` (web), `1025 → 1025` (SMTP) | — | — | — |
| `api` | C-API, C-WORKER | build de `./backend` (`backend/Dockerfile`: SDK 10.0 → `aspnet:10.0`, utilizador `$APP_UID`, `EXPOSE 8080`) | `5080 → 8080` | — | `postgres` saudável, `mailpit` arrancado | `ASPNETCORE_ENVIRONMENT=Development`; `ConnectionStrings__Default` (`Host=postgres;Port=5432;…;GSS Encryption Mode=Disable`); `AdminSeed__Email`/`AdminSeed__Password` (de `ADMIN_SEED_*` no `.env`, vazios por omissão); `Email__Host=mailpit`, `Email__Port=1025`, `Email__Security=None`. |
| `frontend` | C-WEB | build de `./frontend` (`frontend/Dockerfile`: `node:22-alpine`, `output: "standalone"`, utilizador `node`) com o argumento de build `API_INTERNAL_URL` | `3000 → 3000` | — | `api` (arrancado, não saudável) | `API_INTERNAL_URL=http://api:8080`, definido uma vez (`x-api-internal-url`) e usado como argumento de build e como variável de ambiente. |

O que vem do `appsettings.Development.json` (porque o ambiente é Development) e não do compose: `Jwt:SigningKey` (chave só de desenvolvimento), `Email:FromAddress`, `PublicSite:BaseUrl=http://localhost:3000` e `Security:AllowedOrigins` (`http://localhost:3000` e `http://localhost:5080`).

**Consequências do ambiente Development:**

- A API aplica as migrações no arranque (`Program.cs`), corre o seed do admin e mostra o Swagger em `/swagger` (só em Development).
- O browser fala com `http://localhost:3000`; os cookies são `Secure` mas os browsers aceitam-nos em `localhost` sem HTTPS (comentário em `Api/Auth/AuthCookies.cs`).
- A API tem uma segunda entrada, `localhost:5080`, para ferramentas ([Notas de arquitetura](../README.md#limitações-conhecidas), 1). O PostgreSQL também está publicado em `localhost:5432`, para correr a API fora do Docker (`dotnet run`, com a ligação do `appsettings.Development.json`) e para clientes SQL.
- Os dados ficam no volume `postgres-data`: sobrevivem a `docker compose down` e só se apagam com `docker compose down -v`.
- Os comandos de linha (C-CLI) correm como `docker compose run --rm api seed-demo` (ou `set-admin-password`, com `ADMIN_NEW_PASSWORD`): um contentor de curta duração na mesma rede.

### 3. Produção (T06)

![Implantação em produção (T06)](../out/04-deployment-prod.svg)

Fonte: [04-deployment-prod.puml](04-deployment-prod.puml). **Em produção desde 2026-11-09 (T06).** Os passos manuais estão no [runbook](../../deployment/runbook.md).

| Nó ou serviço | Elemento | Corre | Público? | Nota |
| --- | --- | --- | --- | --- |
| Vercel (Hobby) | PL-VERCEL, PL-VERCEL-EDGE | C-WEB | Sim, em `https://<projeto>.vercel.app` | **A origem que o browser conhece.** O rewrite de `/api/*` fica fixado no build. |
| Render (gratuito) | PL-RENDER, PL-RENDER-EDGE, PL-RENDER-API | C-API com C-WORKER, imagem de `backend/Dockerfile` | **Sim**, em `https://<serviço>.onrender.com` | Adormece ao fim de 15 minutos sem tráfego. Deploy automático desligado. |
| Supabase (gratuito) | PL-SUPABASE, PL-SUPAPOOL, C-DB | PostgreSQL gerido e o pooler | Sim (endpoint na Internet, com palavra-passe e TLS) | Sempre pelo pooler em modo sessão (secção 4.6). |
| Brevo | PL-BREVO | SMTP | — (fornecedor) | Porta 2525 com StartTls (confirmado no primeiro deploy); remetente verificado por email. |
| GitHub Actions | X-GHA, PL-DEPLOY, PL-PRODDATA, PL-MIGBUNDLE | `deploy.yml`, `prod-data.yml` | — | Secção 4.8. |
