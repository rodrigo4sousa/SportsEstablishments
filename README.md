# Reservas de Campos

[![CI](https://github.com/rodrigo4sousa/SportsEstablishments/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/rodrigo4sousa/SportsEstablishments/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Next.js 16](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)
![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)

Aplicação web para reservar campos desportivos (futsal, futebol 7, padel, ténis) em clubes portugueses. Os jogadores pesquisam clubes e reservam horários livres; os donos gerem clubes, campos, horários e reservas; o admin da plataforma vê só números agregados. É uma aplicação Next.js com três áreas sobre uma API ASP.NET Core e uma base PostgreSQL. A interface está em pt-PT; o código e os commits estão em inglês.

> **Demo online:** <https://sports-establishments.vercel.app/>. É uma demonstração que pode ser desligada a qualquer momento. A API corre num plano gratuito que adormece sem tráfego, por isso o primeiro pedido pode demorar cerca de um minuto. As contas de demonstração partilham uma palavra-passe pública (ver [Executar localmente](#executar-localmente)): não a uses numa conta real.

## Capturas de ecrã

Com os dados de demonstração. O que cada imagem mostra e como foram feitas: [`docs/images`](docs/images/README.md).

| Pesquisa de clubes | Página do clube | Resumo da reserva semanal | As minhas reservas |
|:---:|:---:|:---:|:---:|
| ![Pesquisa de clubes com filtros de desporto e data](docs/images/screenshot-club-search-results.png) | ![Página do clube com campos, preço base e horários com preços](docs/images/screenshot-club-page-slots-and-prices.png) | ![Resumo da reserva com repetição semanal e pré-visualização das datas](docs/images/screenshot-booking-summary-weekly-repeat.png) | ![As minhas reservas, separador Próximas, com uma série semanal](docs/images/screenshot-my-bookings-upcoming-series.png) |

| Calendário do dono | Estatísticas do clube | Painel do admin | Admin: registos e ocupação |
|:---:|:---:|:---:|:---:|
| ![Calendário do dono, vista diária, com reservas, um bloqueio e uma reserva criada pelo clube](docs/images/screenshot-owner-calendar-day.png) | ![Estatísticas de um clube por campo](docs/images/screenshot-owner-statistics-club.png) | ![Painel do admin com totais, gráficos e lista de donos](docs/images/screenshot-admin-dashboard.png) | ![Novos registos por semana e os 10 clubes com maior ocupação](docs/images/screenshot-admin-top-occupation-registrations.png) |

## Funcionalidades

As histórias e os critérios de aceitação estão no [backlog](docs/backlog.md). Um mercado único: uma conta de jogador serve em todos os clubes de todos os donos.

**Jogador** (`/`)

- Pesquisar clubes por desporto, localidade e data, e ver os horários livres de cada campo com o preço (US16, US17). Sem conta, só se vê; para reservar, entra-se e volta-se à mesma escolha.
- Registo, login e recuperação da palavra-passe (US01, US03, US05).
- Reservar um horário, de hoje a 30 dias, com o preço do momento (US18), e repetir a reserva por 2 a 4 semanas (US20).
- Ver as reservas próximas e passadas e cancelar dentro da janela do clube (US19).
- Emails: confirmação e cancelamento, lembrete antes da reserva e resumo da série (US21, US22).

**Dono** (`/manage`)

- Registo; clubes, campos, horário semanal, duração dos horários, bloqueios e faixas de preço (US02, US06 a US10).
- Calendário diário e semanal, cancelamento com motivo, reservas para clientes sem conta, marcar faltas (US11 a US13, US15).
- Ocupação e receita por clube e campo (US14); aviso por email de cada nova reserva (US23).

**Admin** (`/admin`)

- A conta nasce só de configuração no arranque, nunca por um endpoint (US04).
- Totais, reservas e registos ao longo do tempo, clubes com maior ocupação e lista de donos (US24 a US28). Nunca vê nomes, emails nem telefones de jogadores.
- Desativar e reativar donos, com motivo e histórico só de acrescentar (US29).

## Stack

Next.js 16.4 (App Router), React 19.3, TypeScript 5.9, Tailwind CSS 4.3 e shadcn/ui, TanStack Query, React Hook Form e Zod; ASP.NET Core 10 com controllers, EF Core 10 e Npgsql, ASP.NET Core Identity, FluentValidation e MailKit; PostgreSQL 18. Os tipos da API no front end são gerados a partir do OpenAPI. Testes com xUnit v3, Shouldly, Testcontainers, Vitest e MSW. Docker Compose, GitHub Actions e PlantUML. As versões exatas estão em [`frontend/package.json`](frontend/package.json), [`backend/Directory.Packages.props`](backend/Directory.Packages.props), [`global.json`](global.json) e [`docker-compose.yml`](docker-compose.yml); as razões das escolhas, na [Stack](docs/stack.md).

## Arquitetura

![Contexto: visitante, jogador, dono e admin usam o sistema, que envia email por SMTP](docs/architecture/out/01-context.svg)

![Contentores em produção: browser, Next.js na Vercel, API e worker no Render, PostgreSQL no Supabase e Brevo](docs/architecture/out/02-containers-prod.svg)

![Implantação em produção: Vercel, Render, Supabase, Brevo e GitHub Actions](docs/architecture/out/04-deployment-prod.svg)

Contexto, contentores em produção e implantação em produção. As legendas dos diagramas estão em português. Todas as vistas (C4 e 4+1), o inventário de elementos e as decisões estão em [`docs/architecture`](docs/architecture/README.md).

As decisões principais:

- **Clean Architecture** no back end, em quatro projetos com as dependências só para dentro (Api → Infrastructure → Application → Domain). A Application usa o `IAppDbContext` e não há repositório genérico. Ver a [vista lógica](docs/architecture/4plus1/logical.md).
- **Sem reservas duplicadas pela base de dados.** Uma exclusion constraint do PostgreSQL (`bookings_no_overlap`) impede duas reservas confirmadas sobrepostas no mesmo campo, e a sua violação vira um 409. Bloquear e reservar tomam antes um trinco na linha do campo. Há um teste com 20 pedidos simultâneos para o mesmo horário. [ADR 0003](docs/architecture/adr/0003-exclusion-constraint-reservas.md).
- **Emails por outbox.** A mensagem grava-se na mesma transação da reserva ou do cancelamento, e um worker dentro da API envia-a depois, com `SKIP LOCKED` e novas tentativas. [ADR 0004](docs/architecture/adr/0004-outbox-para-emails.md).
- **Sessão em cookies httpOnly, na mesma origem.** O Next.js reencaminha `/api/*` para a API, por isso o browser só conhece uma origem, os cookies são first-party e não há CORS. Um JWT de 15 minutos e um refresh token rotativo, fora do alcance do JavaScript da página. [ADR 0002](docs/architecture/adr/0002-sessao-em-cookies-httponly.md).
- **Horários calculados, não guardados**, em blocos de tempo real, certos nos dias de mudança de hora. [ADR 0007](docs/architecture/adr/0007-slots-calculados.md).

## Implantação

Em produção desde 2026-11-09: o site na Vercel, a API e o worker de emails num serviço do Render, a PostgreSQL no Supabase, o email no Brevo e os pipelines no GitHub Actions. A decisão está na [ADR 0009](docs/architecture/adr/0009-alvo-de-implantacao.md) e os passos manuais no [runbook](docs/deployment/runbook.md); as variáveis, em [environment-variables.md](docs/deployment/environment-variables.md). O deploy corre depois do CI verde na `main` e só se a variável `DEPLOY_ENABLED` for `true`.

Tudo corre em planos gratuitos, e isso tem custos: a API adormece sem tráfego, e o worker só envia emails enquanto ela estiver acordada; o plano gratuito do Supabase não inclui cópias de segurança automáticas; sem domínio próprio, usam-se os endereços `*.vercel.app` e `*.onrender.com`; e os limites por IP são de melhor esforço, porque a API é pública. Os dados são de demonstração.

## Testes

Contados a 2026-11-09:

- **Back end:** 1410 testes, todos a passar (`dotnet test backend/SportsBooking.slnx`): 553 unitários e 857 de integração. Os de integração correm contra PostgreSQL 18 real, com Testcontainers; precisam de Docker, e sem ele falham todos. Nenhum usa o EF Core em memória.
- **Front end:** 1171 testes a passar (`npm test`, em `frontend/`): Vitest, Testing Library e MSW.
- **Não há testes end-to-end.** As alterações à interface verificam-se à mão no browser, com a stack completa.

```bash
dotnet build backend/SportsBooking.slnx     # os avisos são erros; regenera o OpenAPI
dotnet test backend/SportsBooking.slnx
cd frontend && npm ci && npm run lint && npm run typecheck && npm test && npm run build
npm run gen:api                              # em frontend/, depois de um build do back end
```

O [CI](.github/workflows/ci.yml) corre três jobs em cada push e pull request: o back end (compilação, testes, OpenAPI em dia, modelo EF sem migração em falta, pacotes vulneráveis), o front end (lint, tipos, testes, build, tipos da API em dia, `npm audit` das dependências de produção) e os diagramas (desenha-os e falha se um SVG mudou). A `main` não tem proteção de branch: o resultado do CI confirma-se à mão antes de cada merge.

## Executar localmente

Precisas de Docker com Compose v2.

```bash
cp .env.example .env          # opcional: valores locais; é aqui que se define o admin
docker compose up --build
```

| Serviço | URL |
| --- | --- |
| Aplicação | http://localhost:3000 |
| API | http://localhost:5080 (`/health`) |
| Swagger (só em Development) | http://localhost:5080/swagger |
| Mailpit (caixa de entrada de desenvolvimento) | http://localhost:8025 |
| PostgreSQL | `localhost:5432` (utilizador, palavra-passe e base `sportsbooking`) |

Em Development a API aplica as migrações no arranque. Os dados ficam no volume da base de dados; `docker compose down -v` apaga-o.

**Dados de demonstração.** Com a stack a correr, numa base vazia:

```bash
docker compose run --rm api seed-demo
```

Cria 5 donos, 60 jogadores, 10 clubes, 43 campos, 5 bloqueios e mais de 10 000 reservas (10 612 a 2026-11-09), à volta do Porto e de Lisboa, sem enviar emails, e recusa-se se já existir um dono ou um jogador. Os endereços são `dono01@example.test` a `dono05@example.test` e `jogador01@example.test` a `jogador60@example.test`, e **todas as contas têm a mesma palavra-passe, `demo-password-1`, que é pública e só serve em desenvolvimento**.

**Conta de admin.** Define `ADMIN_SEED_EMAIL` e `ADMIN_SEED_PASSWORD` (8 ou mais caracteres) no `.env`: a API cria o admin no arranque, se ainda não existir. Sem as duas, arranca sem admin e regista um aviso; com só uma, recusa-se a arrancar. O seed nunca altera um admin que já exista, nem cria um segundo, nem promove uma conta de jogador ou de dono.

**Mudar a palavra-passe do admin.** O comando `set-admin-password` muda-a, termina todas as sessões do admin e sai sem arrancar o servidor. A nova palavra-passe só vem da variável de ambiente `ADMIN_NEW_PASSWORD`, nunca da linha de comandos, e nunca é escrita nem registada. Termina com 0 se mudou e com 1 se recusou e não mudou nada.

```bash
read -rs ADMIN_NEW_PASSWORD && export ADMIN_NEW_PASSWORD
docker compose run --rm -e ADMIN_NEW_PASSWORD api set-admin-password
unset ADMIN_NEW_PASSWORD
```

As definições de produção (chave do JWT, origens permitidas, SMTP, papéis da base de dados) estão em [environment-variables.md](docs/deployment/environment-variables.md); a API recusa-se a arrancar se faltar uma obrigatória.

## Documentação

Em português.

- [Backlog](docs/backlog.md): histórias, critérios de aceitação, estimativas e regras de negócio.
- [Stack e práticas](docs/stack.md): a stack, a arquitetura do back end, a autenticação, o front end, os dados, a validação e os erros, os testes e a segurança.
- [Auditoria de UX](docs/ux-audit.md).
- [Arquitetura](docs/architecture/README.md): vistas C4 e 4+1, inventário, [decisões (ADR)](docs/architecture/adr/README.md).
- Implantação: [runbook](docs/deployment/runbook.md), [variáveis de ambiente](docs/deployment/environment-variables.md) e [papéis da base de dados](docs/deployment/supabase-roles.sql).
- [Capturas de ecrã](docs/images/README.md).
- [AGENTS.md](AGENTS.md): as regras de arquitetura e de convenções para quem altera o código, incluindo os comandos.

## Licença e créditos

O repositório não tem ficheiro de licença, por isso não é concedida nenhuma licença. O autor e o contacto estão por preencher. Os nomes de clubes, pessoas e endereços de demonstração são inventados (`example.test`).
