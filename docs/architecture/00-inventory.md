# 00 · Inventário de elementos

Catálogo de tudo o que os diagramas podem usar. Cada elemento tem um **id estável**: os diagramas e as ADRs referem-se a ele por esse id, nunca por um nome solto. Só entra o que existe no código em 2026-10-29 (depois do merge `f4057ca` na `main`); o que está planeado (T06) está numa secção à parte, no fim.

Os caminhos são relativos à raiz do repositório. Para não repetir prefixos longos:

- `Api/` = `backend/src/SportsBooking.Api/`
- `App/` = `backend/src/SportsBooking.Application/`
- `Dom/` = `backend/src/SportsBooking.Domain/`
- `Inf/` = `backend/src/SportsBooking.Infrastructure/`
- `Web/` = `frontend/src/`

**Convenção dos ids:** `P-` pessoas, `X-` sistemas externos, `C-` contentores, `L-` camadas (projetos) do back end, `API-` componentes da API por funcionalidade, `CC-` peças transversais da API, `WEB-` componentes do front end, `D-` entidades e valores do domínio (e entidades persistidas noutras camadas), `DB-` objetos da base de dados, `DEV-` elementos da vista de desenvolvimento (pastas, projetos de teste, jobs de CI, ficheiros gerados), `PL-` elementos planeados. Um id nunca muda nem é reutilizado; um elemento que deixe de existir fica marcado como removido.

## 1. Pessoas e atores

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| P-VIS | Visitante | Pessoa (sem sessão) | Pesquisa clubes, vê a página de um clube e os horários livres; para reservar é enviado para o login. | Rotas públicas `Web/app/(player)/`, `Api/Controllers/ClubsController.cs`, `LocalitiesController.cs` (`[AllowAnonymous]`) |
| P-JOG | Jogador | Pessoa (perfil `Player`) | Regista-se, reserva slots (um ou semanais), vê e cancela as suas reservas, recebe emails. | `Dom/Accounts/Profiles.cs`; `Api/Controllers/BookingsController.cs`, `MeBookingsController.cs`; `Web/app/(player)/bookings/` |
| P-DONO | Dono | Pessoa (perfil `Owner`) | Gere os seus clubes, campos, horários, bloqueios, faixas de preço, calendário, reservas para clientes, faltas e estatísticas. | `Api/Controllers/Manage*Controller.cs`; `Web/app/manage/` |
| P-ADMIN | Admin | Pessoa (perfil `Admin`) | Dono da plataforma: vê contagens, gráficos e a lista de donos, desativa e reativa donos. Criado só pelo seed. | `Api/Controllers/AdminController.cs`; `Inf/Identity/AdminAccountSeeder.cs`; `Web/app/admin/` |
| P-CLI | Cliente sem conta | Pessoa (não usa o sistema) | Pessoa que liga ou aparece no clube; o dono reserva em nome dela (nome e telemóvel). Não recebe email. | `Dom/Bookings/Booking.cs` (`ForCustomer`); `App/Bookings/ClubBookings.cs` (`CreateAsync`) |
| P-OPS | Operador | Pessoa (quem corre a aplicação) | Arranca a stack, corre os comandos de linha (`seed-demo`, `set-admin-password`) e define as variáveis de ambiente. | `docker-compose.yml`, `README.md`, `Api/Program.cs` |

## 2. Sistemas externos

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| X-SMTP | Servidor SMTP | Sistema externo | Recebe os emails da aplicação por SMTP (qualquer servidor; em produção será o Brevo, ver PL-BREVO). | `Inf/Notifications/SmtpEmailSender.cs` (MailKit), `EmailOptions.cs` (`Email:*`) |
| X-MAILPIT | Mailpit | Sistema externo (só desenvolvimento) | Servidor SMTP falso com interface web: apanha todos os emails em desenvolvimento, nenhum sai para fora. | `docker-compose.yml` (serviço `mailpit`, `axllent/mailpit:v1.31.6`, portas 1025 e 8025) |
| X-GHA | GitHub Actions | Sistema externo | CI: build, testes (com Testcontainers), verificação do OpenAPI e dos tipos gerados, migrações pendentes, pacotes vulneráveis, lint, tipos e build do front end. Não faz deploy. | `.github/workflows/ci.yml` |
| X-GH | GitHub | Sistema externo | Repositório, pull requests e proteção da `main` (merge só com o CI verde, squash). | `README.md`, `AGENTS.md` (convenções) |

## 3. Contentores

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| C-WEB | Aplicação Next.js | Contentor (Node.js, Next.js 16 App Router, imagem `standalone`, porta 3000) | Serve as três áreas (`/`, `/manage`, `/admin`) e as páginas de conta. Server Components leem a API interna; Client Components falam com `/api/*` na mesma origem. Reencaminha `/api/*` para a API (WEB-REWRITE): em desenvolvimento faz de proxy reverso. | `frontend/` (`Dockerfile`, `next.config.ts`, `src/`) |
| C-API | API ASP.NET Core | Contentor (.NET 10, controllers, porta 8080; publicada em 5080 no compose) | Única dona das regras de negócio e do acesso à base de dados; autenticação por cookies, autorização por perfil e por dono, ProblemDetails, rate limiting. | `backend/Dockerfile`, `Api/Program.cs` |
| C-WORKER | Worker de emails (outbox) | Contentor lógico (`BackgroundService` dentro do processo da API) | Lê o outbox, compõe e envia os emails por SMTP com novas tentativas; corre em cada instância da API. | `Inf/Notifications/OutboxDispatcher.cs`, `OutboxProcessor.cs` (registado com `AddHostedService`) |
| C-CLI | Comandos de linha da API | Contentor lógico (o mesmo executável da API, sem arrancar o servidor) | `seed-demo` (dados de demonstração) e `set-admin-password` (muda a palavra-passe do admin); correm depois das migrações e do seed do admin e saem. | `Api/Program.cs`; `Inf/Demo/DemoDataCommand.cs`; `Inf/Identity/AdminPasswordCommand.cs` |
| C-DB | Base de dados PostgreSQL | Contentor (PostgreSQL 18.6) | Guarda todos os dados; impõe as regras que não podem falhar (sem reservas sobrepostas, histórico de donos só de acrescentar) e guarda as chaves do Data Protection. | `docker-compose.yml` (serviço `postgres`); `Inf/Persistence/` |
| C-BROWSER | Browser do utilizador | Ambiente de execução (fora do nosso deploy) | Corre os Client Components e guarda os cookies httpOnly de sessão; fala só com C-WEB (mesma origem). | `Web/` (componentes com `"use client"`), `Web/lib/api/client.ts` |

**Proxy reverso, como está hoje:** não há Caddy no repositório. Em desenvolvimento e no compose, o browser fala com C-WEB na porta 3000 e o Next.js reencaminha `/api/*` para `http://api:8080` (WEB-REWRITE). A API também está exposta diretamente na porta 5080 (Swagger em `/swagger`), e `http://localhost:5080` é uma das origens aceites em Development. O Caddy é PL-CADDY.

## 4. Componentes da API por funcionalidade

### Camadas (projetos do back end)

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| L-API | Camada Api | Projeto .NET | Controllers finos, cookies e autenticação, ProblemDetails, segurança HTTP; `Program.cs` compõe. Referencia só L-INF. | `backend/src/SportsBooking.Api/` |
| L-APP | Camada Application | Projeto .NET | Serviços por caso de uso, validação, `Result`, `IAppDbContext` e as interfaces que a Infrastructure implementa. Referencia só L-DOM. | `backend/src/SportsBooking.Application/` |
| L-INF | Camada Infrastructure | Projeto .NET | EF Core e PostgreSQL, Identity, sessões, SMTP, worker do outbox, comandos. Referencia L-APP. | `backend/src/SportsBooking.Infrastructure/` |
| L-DOM | Camada Domain | Projeto .NET | Entidades, valores e regras, sem dependências. | `backend/src/SportsBooking.Domain/` |

### Componentes

Cada componente junta o controller (Api), os serviços (Application) e o que a Infrastructure lhe dá.

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| API-AUTH | Contas e sessões | Componente | Registo de jogador e de dono, login e logout, `GET /api/auth/me`, refresh com rotação, bloqueio por tentativas, recuperação de palavra-passe. | `Api/Controllers/AuthController.cs`; `App/Accounts/`, `App/Auth/`; `Inf/Identity/` (`AccountStore`, `AppUser`, `LoginLockout`, `FallbackPasswordHash`), `Inf/Auth/` (`SessionService`, `RefreshToken`, `PasswordReset`) |
| API-ADMINSEED | Seed do admin | Componente (arranque) | Cria a conta de admin a partir de `AdminSeed:*` no arranque, em todos os ambientes; é o único código que dá o perfil Admin. | `Inf/Identity/AdminAccountSeeder.cs`, `AdminSeedOptions.cs`, `AdminSeedOptionsValidator.cs` |
| API-CLUBS | Clubes do dono | Componente | Criar, listar, ver e editar os clubes do dono (slug, janela de cancelamento, aviso de novas reservas). | `Api/Controllers/ManageClubsController.cs`; `App/Clubs/` (`ClubService`, `IClubStore`); `Inf/Clubs/` |
| API-CATALOG | Catálogo público | Componente | Pesquisa de clubes, localidades, página pública do clube e slots livres de um campo; só leitura, sem dados pessoais. | `Api/Controllers/ClubsController.cs`, `LocalitiesController.cs`; `App/Catalog/` (`ClubSearchService`, `PublicClubService`, `VisibleClubs`) |
| API-PITCHES | Campos | Componente | Criar, editar, desativar e reativar campos (nome único por clube, desporto, preço base). | `Api/Controllers/ManagePitchesController.cs`; `App/Pitches/PitchService.cs`, `OwnedPitches.cs`, `IPitchStore.cs`; `Inf/Pitches/PitchStore.cs`, `PitchConfiguration.cs` |
| API-SCHED | Horários e pré-visualização | Componente | Duração do slot e horário semanal de um campo; pré-visualização dos slots de um dia para o dono. | `Api/Controllers/ManagePitchScheduleController.cs`; `App/Pitches/PitchScheduleService.cs`, `PitchScheduleRequest.cs` |
| API-BLOCKS | Bloqueios | Componente | Bloquear, listar e remover períodos de um campo, recusando sobreposições com bloqueios e reservas. | `Api/Controllers/ManageBlocksController.cs`; `App/Pitches/BlockService.cs`, `BlockRequest.cs`; `Inf/Pitches/BlockConfiguration.cs` |
| API-PRICE | Faixas de preço | Componente | Faixas de preço por campo (dias, horas, preço), sem sobreposições, no máximo 20. | `Api/Controllers/ManagePriceBandsController.cs`; `App/Pitches/PriceBandService.cs`, `PriceBandRequest.cs`; `Inf/Pitches/PitchPriceBandConfiguration.cs` |
| API-BOOK | Reservas do jogador | Componente | Reservar um slot, reservar uma série semanal (pré-visualização e criação), as minhas reservas e cancelar. | `Api/Controllers/BookingsController.cs`, `MeBookingsController.cs`; `App/Bookings/BookingService.cs`, `RecurringBookings.cs`, `MyBookings.cs`, `BookingCancellationService.cs`, `BookingSlotCheck.cs`, `IBookingStore.cs`; `Inf/Bookings/BookingStore.cs` |
| API-CLUBBOOK | Calendário e reservas do dono | Componente | Calendário diário e semanal, detalhes, cancelar com motivo, reservar para um cliente, marcar e anular faltas. | `Api/Controllers/ManageCalendarController.cs`, `ManageBookingsController.cs`; `App/Bookings/ClubCalendar.cs`, `ClubBookings.cs` |
| API-NOTIF | Notificações (outbox) | Componente | Põe os emails no outbox na mesma transação da alteração; compõe os emails (pt-PT) quando são enviados. | `App/Notifications/` (`BookingEmailOutbox`, `BookingEmails`, `BookingNotifications`, `AccountEmails`, `OutboxMessage`); `Inf/Notifications/` (`OutboxProcessor`, `SmtpEmailSender`, `EmailOptions`) |
| API-STATS | Estatísticas do dono | Componente | Ocupação, horas, receita e contagens por campo e por clube num período. | `Api/Controllers/ManageStatisticsController.cs`; `App/Statistics/` (`OccupationService`, `ClubStatisticsService`); `Inf/Statistics/OccupationStore.cs` |
| API-ADMIN | Administração | Componente | Totais, reservas e registos ao longo do tempo, lista de donos, top 10 de ocupação, desativar e reativar donos com histórico. | `Api/Controllers/AdminController.cs`; `App/Admin/`; `Inf/Admin/` (`BookingStatistics`, `OwnerStatusStore`, `OwnerStatusChange`) |
| API-DEMO | Dados de demonstração | Componente (comando) | Cria donos, jogadores, clubes, campos, bloqueios e reservas determinísticos numa base vazia; recusa em produção. | `Inf/Demo/DemoDataCommand.cs`, `DemoDataSeeder.cs`, `DemoCatalog.cs` |
| API-ADMINPW | Mudar a palavra-passe do admin | Componente (comando) | `set-admin-password`: lê `ADMIN_NEW_PASSWORD`, troca o hash e revoga as sessões do admin. | `Inf/Identity/AdminPasswordCommand.cs` |
| API-PERSIST | Persistência | Componente | `IAppDbContext` (vista da Application sobre a base de dados), `AppDbContext`, migrações, registo de comandos falhados, health check. | `App/Common/IAppDbContext.cs`; `Inf/Persistence/` (`AppDbContext`, `DatabaseOptions`, `FailedCommandLogger`, `Migrations/`); `Inf/DependencyInjection.cs` |

### Partes dos componentes mais carregados

Usadas nos diagramas de pormenor da [vista lógica](03-logical-view.md). Cada parte pertence ao componente cujo id é o prefixo do seu.

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| API-BOOK-SINGLE | Uma reserva | Parte de API-BOOK | Reserva de um slot por um jogador. | `App/Bookings/BookingService.cs` |
| API-BOOK-SERIES | Série semanal | Parte de API-BOOK | Pré-visualização e criação de uma série, uma data por transação. | `App/Bookings/RecurringBookings.cs` (`RecurringBookingService`, `RecurringOccurrences`) |
| API-BOOK-MINE | As minhas reservas e cancelar | Parte de API-BOOK | Lista e detalhe das reservas do jogador; cancelamento pelo jogador. | `App/Bookings/MyBookings.cs`, `BookingCancellationService.cs` |
| API-BOOK-STORE | Store das reservas | Parte de API-BOOK (usada também por API-CLUBBOOK) | Insere e grava reservas; 23P01 em `bookings_no_overlap` → `false`. | `App/Bookings/IBookingStore.cs`; `Inf/Bookings/BookingStore.cs` |
| API-AUTH-REG | Registo | Parte de API-AUTH | Registo de jogador e de dono, com início de sessão. | `App/Accounts/AccountRegistrationService.cs` |
| API-AUTH-LOGIN | Login | Parte de API-AUTH | Login com uma só resposta para qualquer falha. | `App/Auth/LoginService.cs` |
| API-AUTH-RESET | Recuperação de palavra-passe | Parte de API-AUTH | Pedido e confirmação; no envio, o token e o texto do email. | `App/Auth/PasswordReset.cs`; `Inf/Auth/PasswordReset.cs` (`PasswordResetStore`, `AccountEmailBuilder`) |
| API-AUTH-ACCOUNTS | Contas no Identity | Parte de API-AUTH | Criar, autenticar, procurar, saber se uma conta está ativa. | `App/Accounts/IAccountStore.cs`; `Inf/Identity/AccountStore.cs`, `FallbackPasswordHash.cs`, `LoginLockout.cs` |
| API-NOTIF-OUTBOX | Pôr no outbox | Parte de API-NOTIF | Junta as mensagens de reservas ao `IAppDbContext` de quem chama. | `App/Notifications/BookingEmailOutbox.cs`, `OutboxMessage.cs` |
| API-NOTIF-DISPATCH | Ciclo do worker | Parte de API-NOTIF (corre em C-WORKER) | `BackgroundService`: um scope por passagem, espera no `TimeProvider`. | `Inf/Notifications/OutboxDispatcher.cs` |
| API-NOTIF-PROC | Processar o outbox | Parte de API-NOTIF (corre em C-WORKER) | Reclama as mensagens devidas (`SKIP LOCKED`), compõe, envia e grava o resultado. | `Inf/Notifications/OutboxProcessor.cs` |
| API-NOTIF-COMPOSE | Compor os emails | Parte de API-NOTIF | Texto e HTML em pt-PT dos emails de reservas e de contas. | `App/Notifications/BookingEmails.cs`, `BookingNotifications.cs`, `AccountEmails.cs` |
| API-NOTIF-SENDER | Enviar por SMTP | Parte de API-NOTIF | `IEmailSender` com MailKit, uma ligação por mensagem. | `App/Notifications/BookingEmails.cs` (`IEmailSender`); `Inf/Notifications/SmtpEmailSender.cs` |

### Peças transversais da API

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| CC-PROBLEM | Erros como ProblemDetails | Transversal | Falhas esperadas como `Result`/`ServiceError` com `code` estável; respostas RFC 9457 com `code` e `traceId`; exceções não tratadas → 500. | `App/Common/Result.cs`, `ErrorCodes.cs`, `ValidationResultExtensions.cs`; `Api/ErrorHandling/` |
| CC-VALID | Validação | Transversal | Regras dos pedidos com FluentValidation, mensagens pt-PT por campo. | `App/**/*Validator*`, `*Request.cs` |
| CC-COOKIES | Cookies de sessão e autenticação | Transversal | JWT de acesso (15 min) e refresh token (7 dias) em cookies httpOnly; o JwtBearer lê o cookie `access_token`. | `Api/Auth/AuthCookies.cs`, `AuthenticationSetup.cs`; `Inf/Auth/SessionService.cs`, `JwtOptions.cs`, `SessionClaims.cs` |
| CC-AUTHZ | Autorização | Transversal | Política de fallback (exige sessão), uma política por perfil, e por dono (T02: `OwnedClubs`, `OwnedPitches`, `OwnedBookings`). | `Api/Auth/AuthenticationSetup.cs`, `HttpCurrentUser.cs`; `App/Common/ICurrentUser.cs`; `App/Clubs/OwnedClubs.cs`; `App/Pitches/OwnedPitches.cs` |
| CC-ORIGIN | Verificação do Origin | Transversal (middleware) | POST/PUT/PATCH/DELETE só com um `Origin` aceite; senão 403 `origin_not_allowed`. | `Api/Security/OriginCheckMiddleware.cs`, `OriginCheckOptions.cs`, `SecuritySetup.cs` |
| CC-RATE | Rate limiting e IP do cliente | Transversal (middleware) | Limites por IP (IPv6 por /64) nas rotas de autenticação e nas de leitura públicas; `X-Forwarded-For` só de proxies configurados. | `Api/Security/RateLimitingSetup.cs`, `AuthRateLimitOptions.cs`, `PublicRateLimitOptions.cs`, `ClientPartition.cs`, `TrustedProxyOptions.cs` |
| CC-TIME | Relógio injetado | Transversal | Toda a hora atual vem de `TimeProvider` (`TimeProvider.System` em execução, falso nos testes). | `Inf/DependencyInjection.cs` (`AddSingleton(TimeProvider.System)`) |
| CC-CLUBTIME | Hora do clube e slots | Transversal (domínio) | Conversão local↔UTC com as regras de mudança de hora, geração de slots por tempo real decorrido, janela de reservas. | `Dom/Scheduling/` (`ClubTime`, `SlotGenerator`, `OpeningHours`, `TimeSlot`, `SlotDuration`, `BookingWindow`) |
| CC-DAYCAL | Slots de um dia e «livre» | Transversal (aplicação) | A única conta de slots de um dia com bloqueios e reservas e a única definição de «livre»; partilhada pela pré-visualização do dono, os slots públicos e a pesquisa. | `App/Pitches/PitchDayCalendar.cs`, `BookingDateInput.cs` |
| CC-SLOTCHECK | Verificação de um slot reservável | Transversal (aplicação) | As regras para reservar um slot (campo ativo, janela, grelha, começado, bloqueios), partilhadas pelas reservas do jogador, do dono e das séries. | `App/Bookings/BookingSlotCheck.cs` |
| CC-LOCK | Trinco do campo | Transversal | Transação com `SELECT … FOR UPDATE` na linha do campo, que serializa reservas, bloqueios, cancelamentos, faltas e faixas de preço. | `App/Pitches/IPitchLock.cs`; `Inf/Pitches/PitchLock.cs` |
| CC-DATAPROT | Chaves do Data Protection | Transversal | Chaves que protegem os tokens de recuperação de palavra-passe, guardadas na base de dados com um nome de aplicação fixo. | `Inf/DependencyInjection.cs` (`PersistKeysToDbContext`); `Inf/Persistence/AppDbContext.cs` |
| CC-OPENAPI | Documento OpenAPI | Transversal | Gerado no build (`openapi/sportsbooking-api.json`), Swagger UI em desenvolvimento, fonte dos tipos do front end. | `Api/openapi/`, `Api/ErrorHandling/ProblemDetailsSchemaTransformer.cs`; `Web/lib/api/schema.d.ts` |
| CC-HEALTH | Health check | Transversal | `GET /health`, com verificação da ligação à base de dados. | `Api/Program.cs`; `Inf/DependencyInjection.cs` (`AddDbContextCheck`) |

## 5. Componentes do front end

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| WEB-AREA-PLAYER | Área do jogador | Grupo de rotas | Pesquisa (`/`), página do clube (`/clubs/[slug]`) e «As minhas reservas» (`/bookings`, só jogadores). | `Web/app/(player)/` |
| WEB-AREA-MANAGE | Área do dono | Grupo de rotas | Clubes, campos, calendário, estatísticas (`/manage/...`). | `Web/app/manage/` |
| WEB-AREA-ADMIN | Área do admin | Grupo de rotas | Painel (`/admin`) e página de um dono (`/admin/owners/[id]`). | `Web/app/admin/` |
| WEB-AREA-ACCOUNT | Páginas de conta | Grupo de rotas | Login, registo de jogador e de dono, pedir e definir nova palavra-passe. | `Web/app/login/`, `register/`, `register/owner/`, `forgot-password/`, `reset-password/` |
| WEB-F-AUTH | Funcionalidade: contas | Funcionalidade | Formulários de login, registo e recuperação; estado da sessão no cabeçalho; `useCurrentUser`. | `Web/features/auth/` |
| WEB-F-CATALOG | Funcionalidade: catálogo | Funcionalidade | Pesquisa no endereço, resultados, detalhes do clube. | `Web/features/catalog/` |
| WEB-F-BOOKING | Funcionalidade: reservar | Funcionalidade | Escolha no endereço (`?pitch=&date=&slot=&weeks=`), lista de slots, resumo, confirmação, série semanal. | `Web/features/booking/` |
| WEB-F-MYBOOK | Funcionalidade: as minhas reservas | Funcionalidade | Separadores e páginas no endereço, cancelar com confirmação, série «2 de 4». | `Web/features/my-bookings/` |
| WEB-F-CLUBS | Funcionalidade: clubes do dono | Funcionalidade | Lista de clubes, formulário do clube (inclui o aviso de novas reservas). | `Web/features/clubs/` |
| WEB-F-PITCHES | Funcionalidade: campos | Funcionalidade | Campos, horário, pré-visualização, bloqueios e faixas de preço (um painel de cada vez). | `Web/features/pitches/` |
| WEB-F-CALENDAR | Funcionalidade: calendário | Funcionalidade | Vista diária e semanal (layout por tempo real), detalhes, cancelar, nova reserva para cliente, faltas. | `Web/features/calendar/` |
| WEB-F-STATS | Funcionalidade: estatísticas | Funcionalidade | Estatísticas do dono por clube e por campo, filtro de período. | `Web/features/statistics/`, `Web/components/period-filter.tsx` |
| WEB-F-ADMIN | Funcionalidade: admin | Funcionalidade | Cartões, gráficos (Recharts), lista de donos, top 10, página do dono e desativar/reativar. | `Web/features/admin/` |
| WEB-API-CLIENT | Cliente da API (browser) | Biblioteca | Única porta para `/api/*` no browser: ProblemDetails → `ApiError`, um refresh partilhado no 401 (Web Lock entre separadores), mensagens por código. | `Web/lib/api/` (`client.ts`, `error-messages.ts`, `form-errors.ts`, `schema.d.ts`) |
| WEB-SERVER | Chamadas à API no servidor | Biblioteca (`server-only`) | Sessão (`/api/auth/me` com o cookie do utilizador), página pública do clube (sem cookies), totais do admin; URL interno da API. | `Web/lib/server/` (`session.ts`, `public-club.ts`, `admin-stats.ts`, `api-url.ts`) |
| WEB-GUARDS | Guardas das áreas | Componentes | Redirecionam por perfil no servidor (conveniência; a API decide sempre). | `Web/components/area-guard.tsx`, `player-guard.tsx`, `guest-only.tsx`; `Web/lib/auth/redirects.ts`, `request-path.ts` |
| WEB-PROXY | `proxy.ts` | Middleware do Next.js | Em `/manage/*`, `/admin/*` e `/bookings`, escreve o caminho pedido no cabeçalho `x-pathname` para o regresso depois do login. Não renova sessões. | `Web/proxy.ts` |
| WEB-REWRITE | Rewrites e cabeçalhos | Configuração do Next.js | Reencaminha `/api/*` para a API interna; cabeçalhos próprios em `/reset-password` (`no-referrer`, `no-store`). | `frontend/next.config.ts` |
| WEB-DATETIME | Datas em hora de Lisboa | Biblioteca | Toda a formatação e aritmética de datas do front end, com as regras de mudança de hora da API. | `Web/lib/datetime.ts` |
| WEB-UI | Componentes partilhados | Biblioteca | shadcn/ui, campos de formulário, cabeçalhos das áreas, erros e carregamento de rota, providers (TanStack Query, toasts). | `Web/components/`, `Web/components/ui/` |

## 6. Entidades do domínio e invariantes

| Id | Nome | Tipo | Invariantes principais | Onde aparece no código |
| --- | --- | --- | --- | --- |
| D-CLUB | Clube | Entidade (Domain) | Um só dono (`OwnerId`); `Slug` único, gerado do nome na criação e nunca mudado; `LocalityKey` acompanha a localidade; janela de cancelamento de 1 a 168 h (24 por omissão); fuso `Europe/Lisbon`. | `Dom/Clubs/Club.cs`, `ClubRules.cs`, `ClubSlug.cs` |
| D-PITCH | Campo | Entidade (Domain) | Nunca é apagado (ativo/inativo); preço base > 0 e ≤ 500, duas casas; nome único no clube (`NameKey`); slot de 30, 60 ou 90 min; os slots só vêm de `SlotsOn` → `SlotGenerator`; o preço de um slot só vem de `PriceForSlot`. | `Dom/Pitches/Pitch.cs`, `PitchRules.cs`, `PitchScheduleRules.cs`, `Sport.cs` |
| D-OPENDAY | Dia de abertura | Entidade dependente (Domain) | No máximo um intervalo por dia da semana; horas de 30 em 30 min; fecho `00:00` = meia-noite, nunca depois da meia-noite. | `Dom/Pitches/PitchOpeningDay.cs`, `Dom/Scheduling/OpeningHours.cs` |
| D-PRICEBAND | Faixa de preço | Entidade dependente (Domain) | Pelo menos um dia; início e fim de 30 em 30 min, fim `00:00` = meia-noite; nunca se sobrepõe a outra do mesmo campo num dia partilhado (tocar é permitido); no máximo 20 por campo. | `Dom/Pitches/PitchPriceBand.cs`, `PriceBandRules.cs` |
| D-BLOCK | Bloqueio | Entidade (Domain) | `[StartsAt, EndsAt)` em UTC com fim depois do início; motivo opcional até 200; só se remove antes de começar. | `Dom/Pitches/Block.cs` |
| D-BOOKING | Reserva | Entidade (Domain) | De um jogador **ou** de um cliente, nunca os dois nem nenhum; nunca apagada, muda de estado (`Confirmed`, `CancelledByPlayer`, `CancelledByClub`, `NoShow`) por métodos que devolvem o resultado; guarda o preço do momento; série semanal opcional (`SeriesId`). | `Dom/Bookings/Booking.cs` |
| D-SLOT | Slot | Valor (Domain) | Intervalo `[início, fim)` em UTC de duração exata; «começado» quando o início ≤ agora. | `Dom/Scheduling/TimeSlot.cs`, `SlotDuration.cs` |
| D-POLICIES | Regras de conta | Valores (Domain) | Email, nome, telemóvel e palavra-passe (≥ 8 caracteres); perfis `Player`, `Owner`, `Admin`. | `Dom/Accounts/` |
| D-USER | Conta | Entidade (Infrastructure, Identity) | Um email = uma conta = um perfil; email normalizado único; `RegisteredAt`; desativada quando `DeactivatedAt` está preenchido. | `Inf/Identity/AppUser.cs` |
| D-REFRESH | Refresh token | Entidade (Infrastructure) | Só o SHA-256 é guardado; rotação condicional atómica; validade absoluta; reutilização revoga todas as sessões da conta. | `Inf/Auth/RefreshToken.cs` |
| D-OUTBOX | Mensagem do outbox | Entidade (Application) | Tipo e payload sem endereços; um só resultado (enviada, ignorada ou falhada); novas tentativas com intervalo a dobrar. | `App/Notifications/OutboxMessage.cs` |
| D-STATUSCHG | Mudança de estado de um dono | Entidade (Infrastructure) | Só se acrescenta (nunca alterada nem apagada); ação, motivo e admin que a fez. | `Inf/Admin/OwnerStatusChange.cs` |

## 7. Objetos da base de dados

| Id | Nome | Tipo | Para quê | Onde aparece no código |
| --- | --- | --- | --- | --- |
| DB-USERS | `users`, `roles`, `user_roles` (e `user_claims`, `user_logins`, `user_tokens`, `role_claims`) | Tabelas (Identity) | Contas, perfis e dados do Identity; `ix_users_normalized_email` (único), `ix_users_registered_at`. | `Inf/Identity/IdentityModelConfiguration.cs`; migrações `AddIdentity`, `AddAccountDetailsAndSessions`, `AddUserRegisteredAt` |
| DB-REFRESH | `refresh_tokens` | Tabela | Hashes dos refresh tokens, `ix_refresh_tokens_token_hash`. | `Inf/Auth/RefreshToken.cs` |
| DB-CLUBS | `clubs` | Tabela | Clubes; `ix_clubs_slug`, `ix_clubs_locality_key`, `ix_clubs_owner_id_name`, `ck_clubs_cancellation_window_hours`. | `Inf/Clubs/ClubConfiguration.cs` |
| DB-PITCHES | `pitches`, `pitch_opening_hours`, `pitch_price_bands` | Tabelas | Campos (`ix_pitches_club_id_name_key` único, `ck_pitches_*`), horário semanal e faixas de preço (`ck_pitch_price_bands_*`). | `Inf/Pitches/PitchConfiguration.cs`, `PitchPriceBandConfiguration.cs` |
| DB-BLOCKS | `blocks` | Tabela | Bloqueios (`ix_blocks_pitch_id_starts_at`). | `Inf/Pitches/BlockConfiguration.cs` |
| DB-BOOKINGS | `bookings` | Tabela | Reservas; `ck_bookings_player_or_customer`, `ck_bookings_status`, `ck_bookings_price`; índices por campo, jogador, criação e série. | `Inf/Bookings/BookingConfiguration.cs` |
| DB-NOOVERLAP | `bookings_no_overlap` | Exclusion constraint (GiST) | Nenhuma reserva confirmada se sobrepõe a outra do mesmo campo: `pitch_id WITH =`, `tstzrange(starts_at, ends_at) WITH &&`, `WHERE status = 'Confirmed'`. Violação = SQLSTATE 23P01 → 409 `slot_taken`. | Migração `AddBookings` (SQL à mão) |
| DB-GIST | `btree_gist` | Extensão | Necessária para o `=` sobre `pitch_id` no índice GiST. | Migração `AddBookings` |
| DB-OUTBOX | `outbox_messages` | Tabela | Emails por enviar e o seu resultado; `ix_outbox_messages_pending_next_attempt_at`, `ck_outbox_messages_one_outcome`. | `Inf/Notifications/OutboxMessageConfiguration.cs` |
| DB-OUTBOXUQ | `ux_outbox_messages_booking_notification` | Índice único parcial | No máximo um lembrete e um aviso ao dono por reserva (`type`, `payload ->> 'bookingId'`). | Migração `AddNoShowNotificationsAndReminders` (SQL à mão) |
| DB-STATUSCHG | `owner_status_changes` | Tabela | Histórico de desativações e reativações de donos; chaves estrangeiras `RESTRICT`. | `Inf/Admin/OwnerStatusChange.cs` |
| DB-AUDITTRG | `owner_status_changes_append_only` | Função e triggers (`owner_status_changes_no_update_delete`, `owner_status_changes_no_truncate`) | Recusa `UPDATE`, `DELETE` e `TRUNCATE` no histórico (SQLSTATE 23001). | Migração `AddOwnerStatusChanges` (SQL à mão) |
| DB-DPKEYS | `data_protection_keys` | Tabela | Chaves do Data Protection (CC-DATAPROT). | Migração `AddDataProtectionKeys` |
| DB-MIGHIST | `__EFMigrationsHistory` | Tabela (EF Core) | Migrações aplicadas. | `Inf/Persistence/Migrations/` |

## 8. Planeado (T06), ainda sem código

Não usar estes ids nos diagramas do estado atual; só nas vistas de implantação futura, marcados como planeados.

| Id | Nome | Tipo | O que se prevê | Fonte |
| --- | --- | --- | --- | --- |
| PL-CADDY | Caddy | Contentor (proxy reverso) | HTTPS, encaminha `/api/*` para a API e o resto para o Next.js na mesma origem; o seu endereço fixo será o `ForwardedHeaders:KnownProxies` da API. | `docs/stack.md` (Deploy), `docs/backlog.md` (T06) |
| PL-HOST | Máquina de produção | Nó de implantação | Uma máquina virtual com docker compose (alternativa: Azure Container Apps). A escolher na ADR de implantação. | `docs/stack.md` (Deploy) |
| PL-GHCR | GitHub Container Registry | Sistema externo | Guarda as imagens construídas pelo CI. | `docs/stack.md` (Deploy) |
| PL-DEPLOY | Workflow de deploy | Pipeline | `deploy.yml`: constrói e publica as imagens, entra por SSH, aplica o migration bundle e faz `docker compose up -d`. | `docs/stack.md`, `docs/backlog.md` (T06) |
| PL-MIGBUNDLE | Migration bundle | Artefacto | Aplica as migrações no deploy como dono da base de dados (a API liga-se com um utilizador sem DDL). | `docs/backlog.md` (T06) |
| PL-DBUSERS | Dois utilizadores da base de dados | Configuração | Dono do esquema (migrações, `btree_gist`) e utilizador da aplicação só com leitura e escrita. | `docs/backlog.md` (T06) |
| PL-BREVO | Brevo | Sistema externo (SMTP) | O servidor SMTP de produção (X-SMTP em produção). | `docs/stack.md`, `docs/backlog.md` (US22, T06) |
| PL-PGMANAGED | PostgreSQL gerido | Sistema externo (alternativa) | PostgreSQL como serviço (por exemplo Azure Database for PostgreSQL) em vez do contentor C-DB. | `docs/stack.md` (Deploy) |
| PL-BACKUP | Cópia diária | Tarefa | `pg_dump` diário da base de dados. | `docs/stack.md` (Deploy) |
| PL-RENDER | Render | Sistema externo (plataforma de alojamento) | Aloja o front end e a API em produção; é uma caixa nos diagramas, sem detalhe. | Plano de produção indicado pelo autor (T06); ainda não está em `docs/stack.md` |
| PL-RENDER-WEB | Serviço web público do Render | Nó de implantação | Corre C-WEB; é a única entrada pública (HTTPS) e reencaminha `/api/*` para a API. | Idem |
| PL-RENDER-API | Serviço privado do Render | Nó de implantação | Corre C-API com C-WORKER, sem URL público; só se alcança pela rede interna do Render. | Idem |
| PL-SUPABASE | Supabase PostgreSQL | Sistema externo (PostgreSQL gerido) | Hospeda a base de dados (C-DB) em produção. | Idem |

**Nota (plano de produção).** `docs/stack.md` e `docs/backlog.md` (T06) descrevem uma máquina virtual com Caddy (PL-HOST, PL-CADDY, PL-GHCR, PL-DEPLOY, PL-MIGBUNDLE). O plano atual, com PL-RENDER, PL-RENDER-WEB, PL-RENDER-API, PL-SUPABASE e PL-BREVO, não tem Caddy: o Next.js (C-WEB) continua a ser o proxy reverso. Os dois ainda não estão reconciliados nesses documentos; os ids antigos ficam até a T06 decidir.

## 9. Vista de desenvolvimento

Elementos que só a [vista de desenvolvimento](05-development-view.md) usa: pastas do repositório, projetos de teste, ficheiros gerados e jobs de CI. Os projetos do back end são as camadas `L-` e as pastas do front end reutilizam os ids `WEB-`.

| Id | Nome | Tipo | Responsabilidade | Onde aparece no código |
| --- | --- | --- | --- | --- |
| DEV-BACKEND | `backend/` | Pasta | Solução .NET: quatro projetos em `src/`, dois de teste em `tests/`, versões centrais dos pacotes, `Dockerfile`. | `backend/` |
| DEV-FRONTEND | `frontend/` | Pasta | Aplicação Next.js (`src/`), configuração, `Dockerfile`. | `frontend/` |
| DEV-DOCS | `docs/` | Pasta | `backlog.md`, `stack.md` e a arquitetura. | `docs/` |
| DEV-ARCHDOCS | `docs/architecture/` | Pasta | Estes documentos, as ADRs e os diagramas (`.puml` e `.svg` lado a lado). | `docs/architecture/` |
| DEV-SCRIPTS | `scripts/` | Pasta | Desenho dos diagramas com a imagem PlantUML fixada no cabeçalho. | `scripts/render-diagrams.sh`, `scripts/render-diagrams.ps1` |
| DEV-WORKFLOWS | `.github/workflows/` | Pasta | Pipeline de CI. | `.github/workflows/ci.yml` |
| DEV-COMPOSE | `docker-compose.yml` | Ficheiro | Stack local: `postgres`, `mailpit`, `api`, `frontend`. | `docker-compose.yml`, `.env.example` |
| DEV-ROOTCFG | Configuração da raiz | Ficheiros | SDK fixado, estilo de código, fins de linha, ficheiros ignorados, README e AGENTS. | `global.json`, `.editorconfig`, `.gitattributes`, `.gitignore`, `README.md`, `AGENTS.md` |
| DEV-TEST-UNIT | Testes unitários do back end | Projeto de teste | Domínio, serviços e peças da API sem base de dados (xUnit v3, Shouldly, `FakeTimeProvider`). | `backend/tests/SportsBooking.UnitTests/` |
| DEV-TEST-INT | Testes de integração do back end | Projeto de teste | Endpoints completos contra PostgreSQL real (Testcontainers), com `ApiFactory`. | `backend/tests/SportsBooking.IntegrationTests/` |
| DEV-TEST-WEB | Testes do front end | Testes | Vitest, Testing Library e MSW; os `*.test.ts(x)` ficam ao lado do código, o apoio em `src/test/`. | `Web/test/`, `frontend/vitest.config.mts`, `frontend/vitest.setup.ts` |
| DEV-WEBTYPES | Tipos da API gerados | Ficheiro gerado | Tipos TypeScript gerados do documento OpenAPI (CC-OPENAPI) por `openapi-typescript`; nunca se editam à mão. | `Web/lib/api/schema.d.ts` |
| DEV-CI-BACKEND | Job `backend` | Job de CI | «Backend (build + tests)». | `.github/workflows/ci.yml` |
| DEV-CI-FRONTEND | Job `frontend` | Job de CI | «Frontend (lint, typecheck, tests, build)». | `.github/workflows/ci.yml` |
| DEV-CI-DIAGRAMS | Job `diagrams` | Job de CI | «Docs (architecture diagrams)»: desenha e compara os SVG. | `.github/workflows/ci.yml` |
