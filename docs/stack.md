# Stack e Boas Práticas — App de Reservas
 
Oct 22, 2026 · @Rodri
 
Next.js (App Router) no front end, ASP.NET Core no back end e PostgreSQL, servidos na mesma origem e com a sessão em cookies httpOnly. A API é a única dona das regras de negócio; o Next.js só apresenta.
 
## Arquitetura
 
&#91;embedded content: arquitetura · 6 componentes\]
 
O browser só fala com o Caddy. Os Server Components chamam a API por dentro, a passar o cookie do utilizador, e nada além da API acede ao PostgreSQL.
 
## Stack
 
Cada escolha tem uma razão; o que não tem razão para estar aqui ficou de fora.
 
| Camada | Tecnologia | Porquê |
| --- | --- | --- |
| Front end | Next.js (App Router), React, TypeScript em modo strict | Server Components lêem dados no servidor com o cookie; Client Components tratam da interação |
| UI | Tailwind CSS e shadcn/ui | Tailwind aparece nas vagas; shadcn/ui dá componentes acessíveis que ficam no teu código |
| Dados do servidor no cliente | TanStack Query | Cache, recarregamento e invalidação depois de reservar ou cancelar |
| Estado de UI | Zustand | Estado partilhado que não vem da API (passo da reserva, vista do calendário) |
| Formulários | React Hook Form e Zod | Validação no browser com mensagens por campo |
| Tipos da API | Gerados do OpenAPI (openapi-typescript) | Os DTOs em C# são a única fonte de verdade; o front end não reescreve tipos à mão |
| Gráficos | Recharts | Gráficos do admin |
| Back end | ASP.NET Core 10 (LTS), Web API com controllers | Controllers são o que a maioria das vagas .NET usa |
| Utilizadores | ASP.NET Core Identity (só o core, sem UI) | Hash de palavras-passe, bloqueio por tentativas e tokens de recuperação sem reinventar |
| Sessão | JWT curto e refresh token, ambos em cookies httpOnly | Ver Autenticação |
| Validação na API | FluentValidation | Regras dos pedidos fora dos controllers e testáveis |
| Acesso a dados | EF Core com Npgsql | Migrações, LINQ e o equivalente ao Prisma que já usaste |
| Base de dados | PostgreSQL | Exclusion constraints para impedir reservas sobrepostas |
| Trabalho em segundo plano | BackgroundService e tabela outbox | Emails e lembretes sem bloquear pedidos e sem se perderem |
| Email | SMTP: Brevo em produção, Mailpit em desenvolvimento | Nenhum email real sai durante o desenvolvimento |
| Testes | xUnit, Testcontainers, Vitest, React Testing Library, MSW, Playwright | Ver Testes |
| Infraestrutura | Docker, docker compose, GitHub Actions, Caddy | Tudo arranca com um comando; Caddy trata do HTTPS e junta front end e API na mesma origem |
 
## Back end: arquitetura e injeção de dependências
 
Clean Architecture em quatro projetos, com as dependências a apontar só para dentro: Api → Infrastructure → Application → Domain.
 
```text
backend/
  src/
    Reservas.Domain/          entidades, regras de negócio, sem dependências
    Reservas.Application/     casos de uso (serviços), DTOs, validadores, interfaces
    Reservas.Infrastructure/  EF Core, migrações, Identity, JWT, email, outbox
    Reservas.Api/             controllers, autenticação, ProblemDetails, composição da DI
  tests/
    Reservas.UnitTests/
    Reservas.IntegrationTests/
```
 
**Pragmatismo, sem cerimónia a mais**
 
- Sem repositório genérico por cima do EF Core: o `DbContext` já é a unidade de trabalho. A Application usa uma interface `IAppDbContext`; um repositório só aparece onde a query for complexa.
- Serviços de aplicação simples (`BookingService.CreateAsync`) em vez de MediatR, e mapeamento à mão em vez de AutoMapper. As duas bibliotecas passaram a ter licença comercial em 2025, e aqui não fazem falta.
**Injeção de dependências**
 
- Contentor nativo do ASP.NET Core e só injeção por construtor; nada de pedir serviços ao `IServiceProvider` no código de negócio.
- Cada camada regista-se a si própria: `services.AddApplication()` e `services.AddInfrastructure(config)`; o `Program.cs` só compõe.
- Tempos de vida: `DbContext` e serviços que o usam são Scoped; helpers sem estado são Singleton. Nunca injetar um Scoped num Singleton: um `BackgroundService` cria o seu scope com `IServiceScopeFactory`.
- A hora atual vem de um `TimeProvider` injetado, nunca de `DateTime.UtcNow`; nos testes usa-se um relógio falso (janela de cancelamento, lembretes).
- Configuração com o padrão Options (`IOptions<JwtOptions>`), validada no arranque com `ValidateOnStart`.
**Regras de código**
 
- Controllers finos: recebem o DTO, chamam o serviço e devolvem a resposta. Nada de EF Core nos controllers.
- As entidades protegem as suas regras: setters privados e métodos com intenção, como `booking.Cancel(motivo, agora)`.
- Falhas esperadas (slot ocupado, fora da janela) são resultados devolvidos pelo serviço, não exceções; exceções ficam para o inesperado.
- `async` em todo o caminho, com `CancellationToken` até ao EF Core.
- Nullable reference types ligados e avisos tratados como erros.
- IDs públicos são GUID v7 (`Guid.CreateVersion7()`): não se adivinham como um número sequencial e indexam bem.
## Autenticação: cookies httpOnly
 
O JavaScript do browser nunca toca nos tokens: ficam em cookies httpOnly, que um ataque XSS não consegue ler. Para saber quem está autenticado, o front end chama `GET /api/auth/me`.
 
**Os dois cookies**
 
| Cookie | Conteúdo | Validade | Atributos |
| --- | --- | --- | --- |
| `access_token` | JWT com `sub` e `role`, sem dados pessoais | 15 minutos | HttpOnly, Secure, SameSite=Lax, Path=/ |
| `refresh_token` | Valor aleatório opaco, guardado na BD só como hash | 7 dias | HttpOnly, Secure, SameSite=Strict, Path=/api/auth |
 
**Fluxo**
 
1. `POST /api/auth/login`: a API valida as credenciais com o Identity e responde com os dois cookies.
2. Cada pedido leva o `access_token` automaticamente; o JwtBearer do ASP.NET Core lê-o do cookie (evento `OnMessageReceived`).
3. Com um 401, o cliente chama `POST /api/auth/refresh` uma vez e repete o pedido. Só há um refresh em curso de cada vez: dentro do separador, os pedidos partilham o mesmo refresh; entre separadores, a Web Locks API (`navigator.locks`) deixa um separador de cada vez renovar, e quem obtém o lock repete primeiro o pedido original, porque outro separador pode já ter renovado (os cookies são partilhados). Sem `navigator.locks`, vale só a regra por separador.
4. Cada refresh roda o token: o antigo fica inválido e sai um novo. Se um token já usado voltar a aparecer, todos os tokens desse utilizador são revogados (sinal de roubo).
5. `POST /api/auth/logout` revoga o refresh token na BD e apaga os dois cookies.
**Mesma origem, obrigatório**
 
O front end e a API têm de estar no mesmo site: o Caddy (em produção) ou os rewrites do Next.js (em desenvolvimento) mandam `/api/*` para o .NET. Assim os cookies são first-party e não há CORS. Front end num domínio `vercel.app` e API noutro domínio tornam os cookies de terceiros, e os browsers bloqueiam-nos.
 
**No Next.js**
 
- Os Server Components leem o cookie com `cookies()` e passam o cabeçalho `Cookie` quando chamam a API.
- Os Server Components não conseguem escrever cookies, e o servidor nunca renova a sessão. Decidido (US03): o cookie `refresh_token` tem `Path=/api/auth`, por isso os pedidos de páginas não o levam e o Next.js não tem como fazer refresh. Não há renovação de tokens no `proxy.ts`; os refreshes vêm sempre do browser.
- Os guardas do servidor vivem nos layouts das áreas (`/manage` para o Dono, `/admin` para o Admin): um helper em `lib/server` (`import 'server-only'`) chama `GET /api/auth/me` no URL interno da API com o cabeçalho `Cookie` do utilizador e `cache: 'no-store'`. Sem sessão válida → `/login?next=<caminho da área>`; sessão de outro perfil → a área desse utilizador. Com `cacheComponents`, o guarda lê cookies, por isso fica dentro de um `<Suspense>`. As páginas públicas (`/`, pesquisa, páginas de clube) ficam abertas a todos.
- O `proxy.ts` (`src/proxy.ts`) só põe o caminho pedido (com a query) no cabeçalho de pedido `x-pathname`, sempre por cima de qualquer valor enviado pelo cliente, e só para `/manage` e `/admin`. Os guardas leem-no para construir `next=<caminho completo>`, que passa por `safeNext`. Não renova tokens, não redireciona e não decide acessos.
- Um access token expirado leva ao `/login?next=...`. A página de login tenta primeiro, no browser, um `POST /api/auth/refresh` silencioso: com 204 segue para `next` (se for um caminho relativo da área do utilizador; senão, a página inicial do perfil), caso contrário mostra o formulário.
- Os guardas são só uma conveniência (redirecionamentos). Quem decide o acesso é sempre a API (401/403).
**CSRF e força bruta**
 
- Como o browser envia os cookies sozinho, os pedidos que alteram dados precisam de proteção: o SameSite=Lax bloqueia POST vindos de outros sites, e a API rejeita POST, PUT, PATCH e DELETE cujo cabeçalho `Origin` não seja o do site.
- Bloqueio de conta do Identity depois de várias tentativas falhadas e rate limiting (middleware nativo) nas rotas `/api/auth/*`.
- Desativar um dono (US29) ou mudar a palavra-passe revoga os refresh tokens; a sessão acaba no máximo quando o access token expira (15 minutos).
- O `POST /api/auth/refresh` verifica sempre que a conta está ativa antes de emitir tokens; uma conta desativada recebe 401 e não renova a sessão.
- **Compromisso conhecido:** o access token é um JWT sem estado, validado sem ir à base de dados. Um token já emitido continua válido até expirar (no máximo 15 minutos), mesmo depois de a conta ser desativada. A validade curta e a verificação no refresh limitam essa janela. Não há verificação de conta ativa em cada pedido.
## Front end: servidor e cliente
 
Server Components por omissão; `'use client'` só onde há interação, e o mais abaixo possível na árvore.
 
| Onde corre | Para quê | Exemplos | Como obtém os dados |
| --- | --- | --- | --- |
| Server Component | Mostrar dados na primeira renderização | Página do clube, lista de reservas, totais do admin | `fetch` à API interna com o cabeçalho `Cookie`, sem cache (`cache: 'no-store'`) |
| Client Component | Interação | Formulários, fluxo de reserva, navegação do calendário, gráficos, filtros | TanStack Query sobre `/api/*` (mesma origem, os cookies seguem sozinhos) |
 
**Regras**
 
- O código que lê cookies ou usa o URL interno da API importa `server-only`, para nunca acabar no bundle do browser.
- Cada área tem o seu `layout.tsx`, que no servidor chama `/api/auth/me` e redireciona se o perfil não corresponder. É conveniência: a API continua a decidir o acesso.
- `loading.tsx` e `error.tsx` em cada segmento de rota; erros de mutações aparecem como toast.
- Só as variáveis `NEXT_PUBLIC_*` chegam ao browser; nenhum segredo usa esse prefixo.
**Estado: TanStack Query vs Zustand**
 
- Dados que vêm da API ficam no TanStack Query. Depois de reservar ou cancelar, invalida as queries afetadas (slots livres, as minhas reservas); com um 409, volta a carregar os slots.
- O Zustand guarda só estado de UI partilhado entre componentes: o passo da reserva (clube, campo, data, slot), a vista do calendário (dia ou semana, campo escolhido).
- Nunca copiar dados da API para o Zustand: cria duas versões da verdade que deixam de bater certo.
- Nunca tokens nem dados pessoais em `persist` ou `localStorage`.
- Uma store pequena por funcionalidade, lida com seletores (`useBookingStore(s => s.date)`) para evitar re-renders.
- Usar as stores só em Client Components. Uma store criada ao nível do módulo é partilhada entre pedidos no servidor; se for precisa no SSR, cria-se uma por pedido com um Provider.
**Formulários com Zod**
 
- React Hook Form com `zodResolver`; um schema por formulário, na pasta da funcionalidade.
- O Zod serve a experiência do utilizador, não a segurança: a API valida sempre outra vez.
- Os erros de validação da API (`errors` no ProblemDetails) são postos no campo certo com `setError`.
**Estrutura**
 
```text
frontend/src/
  app/
    (player)/      início, pesquisa, clube, as minhas reservas
    manage/        área do dono
    admin/         área do admin
    login/  register/
  features/
    booking/       components/ hooks/ schemas/ store.ts
    clubs/  calendar/  admin-stats/
  lib/
    api/           tipos gerados do OpenAPI e cliente do browser
    server/        chamadas à API a partir do servidor (server-only)
  components/ui/   componentes shadcn/ui
```
 
## Dados: EF Core, PostgreSQL e concorrência
 
A regra mais importante da app, nunca haver duas reservas no mesmo campo e hora, vive na base de dados e não só no código.
 
**Reservas sobrepostas (T03)**
 
Uma exclusion constraint, criada numa migração com `migrationBuilder.Sql`:
 
```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
 
ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
  EXCLUDE USING gist (
    pitch_id WITH =,
    tstzrange(starts_at, ends_at) WITH &&
  ) WHERE (status = 'Confirmed');
```
 
- O estado guarda-se como texto (`HasConversion<string>()`) para a condição funcionar.
- Uma violação dá o erro PostgreSQL `23P01` (exclusion\_violation), que a API traduz para 409.
- Bloqueios e reservas estão em tabelas diferentes, e a constraint não cobre as duas. Criar uma reserva e criar um bloqueio começam ambos por trancar a linha do campo (`SELECT … FOR UPDATE`) dentro da transação, e só depois verificam a sobreposição.
**Datas (T04)**
 
- Colunas `timestamptz` e `DateTime` sempre em UTC no C# (o Npgsql recusa `DateTime` sem `Kind` UTC nessas colunas).
- Horários de funcionamento como dia da semana e `TimeOnly`, mais o fuso do clube (`Europe/Lisbon`).
- A geração de slots converte hora local para UTC com `TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon")`.
- Testes para as mudanças de hora de 2026: 29 de março (a hora entre a 1h e as 2h não existe) e 25 de outubro (a hora entre a 1h e as 2h repete-se).
- Regras decididas: uma hora local que não existe avança para depois do salto (01:30 passa a 02:30); uma hora que se repete usa a primeira ocorrência (hora de verão). Os slots são blocos de tempo real a partir da abertura, por isso nesses dias há menos um ou mais um slot. Na hora repetida, a UI mostra «(verão)» ou «(inverno)».
- Limitação conhecida do MVP: o fecho mais tarde é às 00:00; horários que passam da meia-noite não são suportados.
**Queries**
 
- Leituras com `AsNoTracking()` e projeção direta para DTOs com `Select`; nunca devolver entidades na API.
- Listas sempre paginadas e com ordem definida.
- Índices nas colunas dos filtros: reservas por campo e início, reservas por jogador e início, clubes por localidade.
- Estatísticas do admin com `GROUP BY`, `COUNT(DISTINCT …)` e `date_trunc`; os períodos sem reservas são preenchidos com zero (no serviço, ou com `generate_series` em SQL).
- Dinheiro em `numeric(10,2)` e `decimal` no C#, nunca `float`.
**Migrações**
 
- Versionadas no repositório; uma migração já aplicada nunca se edita, corrige-se com outra.
- Em produção aplicam-se no deploy com um migration bundle (`dotnet ef migrations bundle`), não no arranque da API.
- Os dados de demonstração (T05) ficam fora das migrações.
## Validação e erros
 
Três camadas de validação, cada uma com o seu papel, e todos os erros da API no mesmo formato: ProblemDetails (RFC 9457).
 
| Camada | Ferramenta | Valida | Exemplo |
| --- | --- | --- | --- |
| Browser | Zod | Forma dos dados, para feedback imediato | Email válido, palavra-passe com 8 caracteres |
| API | FluentValidation | Forma dos pedidos, sempre, venha de onde vier | Data de fim depois da de início |
| Domínio | Métodos das entidades e serviços | Regras de negócio | Cancelamento dentro da janela, slot livre |
 
**Códigos HTTP**
 
| Código | Quando |
| --- | --- |
| 400 | Pedido inválido; o ProblemDetails traz `errors` por campo |
| 401 | Sem sessão ou token expirado |
| 403 | Sessão válida, perfil errado |
| 404 | Não existe, ou pertence a outro dono |
| 409 | Conflito: slot ocupado, sobreposição com bloqueio |
| 422 | Regra de negócio violada: cancelamento fora da janela, campo inativo |
| 429 | Demasiados pedidos (rate limiting) |
| 500 | Erro inesperado: registado no log, sem detalhes internos na resposta |
 
**Regras**
 
- `AddProblemDetails()` e um `IExceptionHandler` global; nenhum controller tem `try/catch` para erros genéricos.
- Cada erro leva um `code` estável (por exemplo, `slot_taken`, `outside_cancellation_window`) e o `traceId`. O front end escolhe a mensagem pelo `code`, nunca pelo texto.
- Nada de stack traces nem mensagens de exceção nas respostas em produção.
## Testes
 
Os testes de integração correm contra um PostgreSQL real, porque as regras mais importantes (sobreposição, fusos) só existem lá.
 
| Tipo | Ferramentas | O que cobre | Exemplos |
| --- | --- | --- | --- |
| Unitários (back end) | xUnit, Shouldly, `FakeTimeProvider` | Regras do domínio e dos serviços | Janela de cancelamento, geração de slots nas mudanças de hora |
| Integração (back end) | xUnit, `WebApplicationFactory`, Testcontainers (PostgreSQL) | Endpoints completos, da autenticação à base de dados | Dono A não vê dados do dono B (T02); pedidos simultâneos para o mesmo slot (T03); códigos e `code` dos erros |
| Componentes (front end) | Vitest, React Testing Library, MSW | Comportamento da UI com a API simulada | Erros por campo no registo; mensagem e lista atualizada depois de um 409 |
| End-to-end | Playwright | Três fluxos críticos na app a correr | Jogador reserva; dono cancela com motivo; admin vê os totais |
 
**Regras**
 
- O provider em memória do EF Core não serve para integração: não aplica constraints nem transações como o PostgreSQL.
- Cada bug encontrado ganha primeiro um teste que o reproduz, e só depois a correção.
- Sem meta de percentagem de cobertura: o que tem de estar coberto são as regras de negócio e os casos de erro.
- Evitar o FluentAssertions 8, que passou a licença comercial; o Shouldly (ou os `Assert` do xUnit) chegam.
## Segurança: checklist
 
Para ir marcando à medida que fica feito.
 
- [x] Política de fallback que exige autenticação em todos os endpoints; os públicos (pesquisa, página do clube, login, registo) levam `[AllowAnonymous]` de forma explícita. (US01)
- [x] Autorização por perfil e por dono em todos os endpoints de clube, com testes (T02, `OwnedClubs`; o teste de acesso descobre os endpoints novos sozinho). (US06)
- [x] Cookies httpOnly, Secure e SameSite como na secção de autenticação; verificação do `Origin` em POST, PUT, PATCH e DELETE. (US01)
- [x] Conta de admin só por seed; nenhum caminho na API dá o perfil Admin (US04, com teste de guarda).
- [x] Bloqueio de conta do Identity e rate limiting nas rotas de autenticação (login, registo, refresh e os dois passos da recuperação de palavra-passe). (US03, US05)
- [x] Mensagens genéricas no login e na recuperação de palavra-passe (não revelam se o email existe). (US03, US05: o pedido responde sempre o mesmo 202 e a confirmação dá sempre o mesmo 400 para uma ligação má.)
- [ ] HTTPS em todo o lado e HSTS em produção.
- [ ] Cabeçalhos de segurança: Content-Security-Policy, X-Content-Type-Options, Referrer-Policy.
- [ ] Segredos em variáveis de ambiente (user-secrets em desenvolvimento); `.env` no `.gitignore`.
- [ ] SQL à mão só com parâmetros (`FromSql` com interpolação, que parametriza); nunca concatenar strings.
- [ ] Nada de palavras-passe, tokens nem cookies nos logs.
- [ ] Admin vê só contagens, nunca nomes ou emails de jogadores.
- [ ] Dependabot ligado, e `dotnet list package --vulnerable` e `npm audit` no CI.
## Repositório, Git, CI e deploy
 
Um só repositório, `main` protegida e deploy automático de tudo o que passa no CI.
 
```text
/
  backend/
  frontend/
  docs/                backlog, diagramas C4, decisões (ADRs)
  docker-compose.yml
  Caddyfile
  .github/workflows/   ci.yml, deploy.yml
```
 
**Git**
 
- `main` protegida: só recebe pull requests com o CI verde, mesmo trabalhando sozinho.
- Branches curtas com o ID da história (`feat/US18-reservar-slot`) e squash merge.
- Commits no formato Conventional Commits (`feat:`, `fix:`, `test:`), com o ID da história.
- Decisões importantes em ADRs curtos em `docs/` (uma app em vez de duas, cookies em vez de localStorage, a exclusion constraint).
**CI (GitHub Actions)**
 
- Back end: restore, build e testes, incluindo os de integração com Testcontainers (os runners Ubuntu do GitHub têm Docker).
- Front end: lint, verificação de tipos, testes e build.
- Playwright só nos pull requests para a `main`, por ser o mais lento.
- Verificação de pacotes vulneráveis em ambos.
**Deploy**
 
- Uma máquina virtual com docker compose: Caddy, Next.js (imagem com `output: 'standalone'`), API e PostgreSQL.
- O CI constrói as imagens e publica-as no GitHub Container Registry; o deploy entra por SSH, aplica o migration bundle e faz `docker compose up -d`.
- O Caddy trata dos certificados HTTPS e encaminha `/api/*` para a API e o resto para o Next.js, na mesma origem.
- Cópia diária da base de dados com `pg_dump`.
- Alternativa, se quiseres Azure no CV: Azure Container Apps com Azure Database for PostgreSQL. Confirma os custos e os níveis gratuitos antes de escolher.
## Fontes
 
- [MediatR and MassTransit Going Commercial: What This Means For You](https://www.milanjovanovic.tech/blog/mediatr-and-masstransit-going-commerical-what-this-means-for-you) — MediatR e AutoMapper passam a licença comercial (2025).
- [Fluent Assertions shifts to restrictive license](https://www.devclass.com/development/2025/01/16/another-open-source-project-shifts-to-restrictive-license-fluent-assertions-following-xceed-partnership/1621343) — a versão 8 exige licença paga para uso comercial; a 7 continua gratuita.
- [Next.js Proxy Replaces Middleware](https://johnkavanagh.co.uk/articles/next-js-proxy-replaces-middleware/) — no Next.js 16, `middleware.ts` passa a `proxy.ts`.
O resto do documento vem de conhecimento geral das ferramentas, sem fonte específica; confirma as versões na documentação oficial quando criares o projeto.